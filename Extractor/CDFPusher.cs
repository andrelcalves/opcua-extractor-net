﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Cognite.Sdk;
using Cognite.Sdk.Api;
using Cognite.Sdk.Assets;
using Cognite.Sdk.Timeseries;
using Opc.Ua;
using Prometheus.Client;

namespace Cognite.OpcUa
{
    /// <summary>
    /// Pusher against CDF
    /// </summary>
    public class CDFPusher : IPusher
    {
        private readonly IHttpClientFactory clientFactory;
        private readonly CogniteClientConfig config;
        private readonly BulkSizes bulkConfig;
        private readonly IDictionary<string, bool> nodeIsHistorizing = new Dictionary<string, bool>();
        private readonly IDictionary<NodeId, long> nodeToAssetIds = new Dictionary<NodeId, long>();
        public Extractor Extractor { private get; set; }
        public UAClient UAClient { private get; set; }
        private NodeId _rootId;
        public NodeId RootNode { get { return _rootId; } set { nodeToAssetIds.Add(value.ToString(), rootAsset); _rootId = value; } }
        public ISet<string> NotInSync { get; }  = new HashSet<string>();
        public object NotInSyncLock { get; } = new object();

        private readonly long rootAsset = -1;

        public CDFPusher(IHttpClientFactory clientFactory, FullConfig config)
        {
            this.config = config.CogniteConfig;
            this.clientFactory = clientFactory;
            bulkConfig = config.BulkSizes;
            rootAsset = config.CogniteConfig.RootAssetId;
        }

        private static readonly Counter dataPointsCounter = Metrics
            .CreateCounter("opcua_datapoints_pushed", "Number of datapoints pushed to CDF");
        private static readonly Counter dataPointPushes = Metrics
            .CreateCounter("opcua_datapoint_pushes", "Number of times datapoints have been pushed to CDF");
        private static readonly Counter dataPointPushFailures = Metrics
            .CreateCounter("opcua_datapoint_push_failures", "Number of completely failed pushes of datapoints to CDF");
        private static readonly Gauge trackedAssets = Metrics
            .CreateGauge("opcua_tracked_assets", "Number of objects on the opcua server mapped to assets in CDF");
        private static readonly Gauge trackedTimeseres = Metrics
            .CreateGauge("opcua_tracked_timeseries", "Number of variables on the opcua server mapped to timeseries in CDF");
        private static readonly Counter nodeEnsuringFailures = Metrics
            .CreateCounter("opcua_node_ensure_failures",
            "Number of completely failed requests to CDF when ensuring assets/timeseries exist");

        #region Interface
        /// <summary>
        /// Dequeues up to 100000 points from the queue, then pushes them to CDF. On failure, writes to file if enabled.
        /// </summary>
        /// <param name="dataPointQueue">Queue to be emptied</param>
        public async Task PushDataPoints(ConcurrentQueue<BufferedDataPoint> dataPointQueue)
        {
            var dataPointList = new List<BufferedDataPoint>();

            int count = 0;
            while (dataPointQueue.TryDequeue(out BufferedDataPoint buffer) && count++ < 100000)
            {
                if (buffer.timestamp > 0L)
                {
                    dataPointList.Add(buffer);
                }
            }

            if (count == 0) return;
            if (config.Debug) return;
            var finalDataPoints = dataPointList.GroupBy(dp => dp.Id, (id, points) =>
                new DataPointsWritePoco
                {
                    Identity = Identity.ExternalId(id),
                    DataPoints = points.Select(point => new DataPointPoco
                    {
                        TimeStamp = point.timestamp,
                        Value = Numeric.Float(point.doubleValue)
                    })
                });

            Logger.LogInfo("Push " + count + " datapoints to CDF");
            using (HttpClient httpClient = clientFactory.CreateClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                Client client = Client.Create(httpClient)
                    .AddHeader("api-key", config.ApiKey)
                    .SetProject(config.Project);
                if (!await Utils.RetryAsync(async () => await client.InsertDataAsync(finalDataPoints), "Failed to insert into CDF"))
                {
                    Logger.LogError("Failed to insert " + count + " datapoints into CDF");
                    dataPointPushFailures.Inc();
                    if (config.BufferOnFailure && !string.IsNullOrEmpty(config.BufferFile))
                    {
                        try
                        {
                            Utils.WriteBufferToFile(dataPointList, config, nodeIsHistorizing);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("Failed to write buffer to file");
                            Logger.LogException(ex);
                        }
                    }
                }
                else
                {
                    if (config.BufferOnFailure && !Utils.BufferFileEmpty && !string.IsNullOrEmpty(config.BufferFile))
                    {
                        Utils.ReadBufferFromFile(dataPointQueue, config, nodeIsHistorizing);
                    }
                    dataPointsCounter.Inc(count);
                    dataPointPushes.Inc();
                }
            }
        }
        /// <summary>
        /// Empty queue, fetch info for each relevant node, test results against CDF, then synchronize any variables
        /// </summary>
        /// <param name="nodeQueue">Queue to be emptied</param>
        public async Task<bool> PushNodes(ConcurrentQueue<BufferedNode> nodeQueue)
        {
            var nodeMap = new Dictionary<string, BufferedNode>();
            var assetList = new List<BufferedNode>();
            var varList = new List<BufferedVariable>();
            var histTsList = new List<BufferedVariable>();
            var tsList = new List<BufferedVariable>();

            bool allOk = true;
            while (nodeQueue.TryDequeue(out BufferedNode buffer))
            {
                if (buffer.IsVariable && buffer is BufferedVariable buffVar)
                {
                    if (buffVar.IsProperty)
                    {
                        nodeMap.TryGetValue(UAClient.GetUniqueId(buffVar.ParentId), out BufferedNode parent);
                        if (parent == null) continue;
                        if (parent.properties == null)
                        {
                            parent.properties = new List<BufferedVariable>();
                        }
                        parent.properties.Add(buffVar);
                    }
                    else
                    {
                        varList.Add(buffVar);
                    }
                }
                else
                {
                    assetList.Add(buffer);
                }
                nodeMap.Add(UAClient.GetUniqueId(buffer.Id), buffer);
            }
            if (varList.Count == 0 && assetList.Count == 0) return true;
            Logger.LogInfo("Getting data for " + varList.Count + " variables and " + assetList.Count + " objects");
            try
            {
                UAClient.ReadNodeData(assetList.Concat(varList));
            }
            catch (Exception e)
            {
                Logger.LogError("Failed to read node data");
                Logger.LogException(e);
            }
            foreach (var node in varList)
            {
                if (Extractor.AllowTSMap(node))
                {
                    if (node.Historizing)
                    {
                        histTsList.Add(node);
                        lock (NotInSyncLock)
                        {
                            NotInSync.Add(UAClient.GetUniqueId(node.Id));
                        }
                    }
                    else
                    {
                        tsList.Add(node);
                    }
                }
            }
            Logger.LogInfo("Testing " + (varList.Count + assetList.Count) + " nodes against CDF");
            if (config.Debug)
            {
                Extractor.SynchronizeNodes(tsList.Concat(histTsList));
                return true;
            }
            using (HttpClient httpClient = clientFactory.CreateClient())
            {
                Client client = Client.Create(httpClient)
                    .AddHeader("api-key", config.ApiKey)
                    .SetProject(config.Project);
                try
                {
                    foreach (var assets in Utils.ChunkBy(assetList, bulkConfig.CDFAssets))
                    {
                        allOk &= await EnsureAssets(assets, client);
                    }
                    trackedAssets.Inc(assetList.Count);
                    // At this point the assets should all be synchronized and mapped
                    // Now: Try get latest TS data, if this fails, then create missing and retry with the remainder. Similar to assets.
                    // This also sets the LastTimestamp property of each BufferedVariable
                    // Synchronize TS with CDF, also get timestamps. Has to be done in three steps:
                    // Get by externalId, create missing, get latest timestamps. All three can be done by externalId.
                    // Eventually the API will probably support linking TS to assets by using externalId, for now we still need the
                    // node to assets map.
                    // We only need timestamps for historizing timeseries, and it is much more expensive to get latest compared to just
                    // fetching the timeseries itself
                    foreach (var timeseries in Utils.ChunkBy(tsList, bulkConfig.CDFTimeseries))
                    {
                        allOk &= await EnsureTimeseries(timeseries, client);
                    }
                    trackedTimeseres.Inc(tsList.Count);

                    foreach (var timeseries in Utils.ChunkBy(histTsList, bulkConfig.CDFTimeseries))
                    {
                        allOk &= await EnsureHistorizingTimeseries(timeseries, client);
                    }
                    trackedTimeseres.Inc(histTsList.Count);
                }
                catch (Exception e)
                {
                    allOk = false;
                    Logger.LogError("Failed to push to CDF");
                    Logger.LogException(e);
                }
            }
            // This can be done in this thread, as the history read stuff is done in separate threads, so there should only be a single
            // createSubscription service called here
            try
            {
                Extractor.SynchronizeNodes(tsList.Concat(histTsList));
            }
            catch (Exception e)
            {
                allOk = false;
                Logger.LogError("Failed to synchronize nodes");
                Logger.LogException(e);
            }
            return allOk;
        }
        /// <summary>
        /// Reset the pusher, preparing it to be restarted
        /// </summary>
        public void Reset()
        {
            nodeToAssetIds.Clear();
            trackedAssets.Set(0);
            trackedTimeseres.Set(0);
        }
        #endregion

        #region Pushing
        /// <summary>
        /// Test if given list of assets exists, then create any that do not, checking for properties.
        /// </summary>
        /// <param name="assetList">List of assets to be tested</param>
        /// <param name="client">Cognite client to be used</param>
        private async Task<bool> EnsureAssets(IEnumerable<BufferedNode> assetList, Client client)
        {
            var assetIds = assetList.ToDictionary(node => UAClient.GetUniqueId(node.Id));
            // TODO: When v1 gets support for ExternalId on assets when associating timeseries, we can drop a lot of this.
            // Specifically anything related to NodeToAssetIds
            ISet<string> missingAssetIds = new HashSet<string>();

            Logger.LogInfo("Test " + assetList.Count() + " assets");
            bool allOk = true;

            var assetIdentities = assetIds.Keys.Select(Identity.ExternalId);
            try
            {
                var readResults = await Utils.RetryAsync(() => client.GetAssetsByIdsAsync(assetIdentities), "Failed to get assets", true);
                if (readResults != null)
                {
                    Logger.LogInfo("Found " + readResults.Count() + " assets");
                    foreach (var resultItem in readResults)
                    {
                        nodeToAssetIds.TryAdd(UAClient.GetUniqueId(assetIds[resultItem.ExternalId].Id), resultItem.Id);
                    }
                }
                else
                {
                    Logger.LogError("Failed to get assets");
                    allOk = false;
                    nodeEnsuringFailures.Inc();
                }
            }
            catch (ResponseException ex)
            {
                if (ex.Code == 400)
                {
                    foreach (var missing in ex.Missing)
                    {
                        if (missing.TryGetValue("externalId", out ErrorValue value))
                        {
                            missingAssetIds.Add(value.ToString());
                        }
                    }
                    Logger.LogInfo("Found " + ex.Missing.Count() + " missing assets");
                }
                else
                {
                    allOk = false;
                    nodeEnsuringFailures.Inc();
                    Logger.LogError("Failed to fetch asset ids");
                    Logger.LogException(ex);
                }
            }
            if (missingAssetIds.Any())
            {
                Logger.LogInfo("Create " + missingAssetIds.Count + " new assets");

                var getMetaData = missingAssetIds.Select(id => assetIds[id]);
                try
                {
                    UAClient.GetNodeProperties(getMetaData);
                }
                catch (Exception e)
                {
                    Logger.LogException(e);
                }
                var createAssets = missingAssetIds.Select(id => NodeToAsset(assetIds[id]));
                var writeResults = await Utils.RetryAsync(() => client.CreateAssetsAsync(createAssets), "Failed to create assets");
                if (writeResults != null)
                {
                    foreach (var resultItem in writeResults)
                    {
                        nodeToAssetIds.TryAdd(UAClient.GetUniqueId(assetIds[resultItem.ExternalId].Id), resultItem.Id);
                    }
                }
                else
                {
                    Logger.LogError("Failed to create assets");
                    allOk = false;
                    nodeEnsuringFailures.Inc();
                }
                var idsToMap = assetIds.Keys
                    .Where(id => !missingAssetIds.Contains(id))
                    .Select(Identity.ExternalId);

                if (idsToMap.Any())
                {
                    Logger.LogInfo("Get remaining " + idsToMap.Count() + " assetids");
                    var readResults = await Utils.RetryAsync(() => client.GetAssetsByIdsAsync(idsToMap), "Failed to get asset ids");
                    if (readResults != null)
                    {
                        foreach (var resultItem in readResults)
                        {
                            nodeToAssetIds.TryAdd(UAClient.GetUniqueId(assetIds[resultItem.ExternalId].Id), resultItem.Id);
                        }
                    }
                    else
                    {
                        Logger.LogError("Failed to get asset ids");
                        allOk = false;
                        nodeEnsuringFailures.Inc();
                    }
                }
            }
            return allOk;
        }
        /// <summary>
        /// Test if given list of timeseries exists, then create any that do not, checking for properties.
        /// </summary>
        /// <param name="tsList">List of timeseries to be tested</param>
        /// <param name="client">Cognite client to be used</param>
        private async Task<bool> EnsureTimeseries(IEnumerable<BufferedVariable> tsList, Client client)
        {
            if (!tsList.Any()) return true;
            var tsIds = new Dictionary<string, BufferedVariable>();
            foreach (BufferedVariable node in tsList)
            {
                string externalId = UAClient.GetUniqueId(node.Id);
                tsIds.Add(externalId, node);
                nodeIsHistorizing[externalId] = false;
            }

            Logger.LogInfo("Test " + tsIds.Keys.Count + " timeseries");
            var missingTSIds = new HashSet<string>();
            bool allOk = true;
            try
            {
                var readResults = await Utils.RetryAsync(() =>
                    client.GetTimeseriesByIdsAsync(tsIds.Keys), "Failed to get timeseries", true);
                if (readResults != null)
                {
                    Logger.LogInfo("Found " + readResults.Count() + " timeseries");
                }
                else
                {
                    Logger.LogError("Failed to get timeseries");
                    allOk = false;
                    nodeEnsuringFailures.Inc();
                }
            }
            catch (ResponseException ex)
            {
                if (ex.Code == 400 && ex.Missing.Any())
                {
                    foreach (var missing in ex.Missing)
                    {
                        if (missing.TryGetValue("externalId", out ErrorValue value))
                        {
                            missingTSIds.Add(value.ToString());
                        }
                    }
                    Logger.LogInfo("Found " + ex.Missing.Count() + " missing timeseries");
                }
                else
                {
                    allOk = false;
                    nodeEnsuringFailures.Inc();
                    Logger.LogError("Failed to fetch timeseries data");
                    Logger.LogException(ex);
                }
            }
            if (missingTSIds.Any())
            {
                Logger.LogInfo("Create " + missingTSIds.Count + " new timeseries");

                var getMetaData = missingTSIds.Select(id => tsIds[id]);
                UAClient.GetNodeProperties(getMetaData);
                var createTimeseries = getMetaData.Select(VariableToTimeseries);
                if (await Utils.RetryAsync(() => client.CreateTimeseriesAsync(createTimeseries), "Failed to create TS") == null)
                {
                    Logger.LogError("Failed to create TS");
                    allOk = false;
                    nodeEnsuringFailures.Inc();
                }
            }
            return allOk;
        }
        /// <summary>
        /// Try to get latest timestamp from given list of timeseries, then create any not found and try again
        /// </summary>
        /// <param name="tsList">List of timeseries to be tested</param>
        /// <param name="client">Cognite client to be used</param>
        private async Task<bool> EnsureHistorizingTimeseries(IEnumerable<BufferedVariable> tsList, Client client)
        {
            if (!tsList.Any()) return true;
            var tsIds = new Dictionary<string, BufferedVariable>();
            bool allOk = true;
            foreach (BufferedVariable node in tsList)
            {
                string externalId = UAClient.GetUniqueId(node.Id);
                tsIds.Add(externalId, node);
                nodeIsHistorizing[externalId] = true;
            }

            Logger.LogInfo("Test " + tsIds.Keys.Count + " historizing timeseries");
            var missingTSIds = new HashSet<string>();

            var pairedTsIds = tsIds.Keys.Select<string, (Identity, string)>(key => (Identity.ExternalId(key), null));
            try
            {
                var readResults = await Utils.RetryAsync(() =>
                    client.GetLatestDataPointAsync(pairedTsIds), "Failed to get historizing timeseries", true);
                if (readResults != null)
                {
                    Logger.LogInfo("Found " + readResults.Count() + " historizing timeseries");
                    foreach (var resultItem in readResults)
                    {
                        if (resultItem.DataPoints.Any())
                        {
                            tsIds[resultItem.ExternalId.Value].LatestTimestamp =
                                DateTimeOffset.FromUnixTimeMilliseconds(resultItem.DataPoints.First().TimeStamp).DateTime;
                        }
                    }
                }
                else
                {
                    Logger.LogError("Failed to get historizing timeseries");
                    allOk = false;
                    nodeEnsuringFailures.Inc();
                }
            }
            catch (ResponseException ex)
            {
                if (ex.Code == 400 && ex.Missing.Any())
                {
                    foreach (var missing in ex.Missing)
                    {
                        if (missing.TryGetValue("externalId", out ErrorValue value) && value != null)
                        {
                            missingTSIds.Add(value.ToString());
                        }
                    }
                    Logger.LogInfo("Found " + ex.Missing.Count() + " missing historizing timeseries");
                }
                else
                {
                    allOk = false;
                    nodeEnsuringFailures.Inc();
                    Logger.LogError("Failed to fetch historizing timeseries data");
                    Logger.LogException(ex);
                }
            }
            if (missingTSIds.Any())
            {
                Logger.LogInfo("Create " + missingTSIds.Count + " new historizing timeseries");

                var getMetaData = missingTSIds.Select(id => tsIds[id]);
                UAClient.GetNodeProperties(getMetaData);
                var createTimeseries = getMetaData.Select(VariableToTimeseries);

                if (await Utils.RetryAsync(() => client.CreateTimeseriesAsync(createTimeseries), "Failed to create historizing TS") == null)
                {
                    Logger.LogError("Failed to create historizing TS");
                    allOk = false;
                    nodeEnsuringFailures.Inc();
                }

                var idsToMap = tsIds.Keys
                    .Where(key => !missingTSIds.Contains(key))
                    .Select<string, (Identity, string)>(key => (Identity.ExternalId(key), null));

                if (idsToMap.Any())
                {
                    Logger.LogInfo("Get remaining " + idsToMap.Count() + " historizing timeseries ids");
                    var readResults = await Utils.RetryAsync(() => client.GetLatestDataPointAsync(idsToMap),
                        "Failed to get historizing timeseries ids");
                    if (readResults != null)
                    {
                        foreach (var resultItem in readResults)
                        {
                            if (resultItem.DataPoints.Any())
                            {
                                tsIds[resultItem.ExternalId.Value].LatestTimestamp =
                                    DateTimeOffset.FromUnixTimeMilliseconds(resultItem.DataPoints.First().TimeStamp).DateTime;
                            }
                        }
                    }
                    else
                    {
                        Logger.LogError("Failed to get historizing timeseries ids");
                        allOk = false;
                        nodeEnsuringFailures.Inc();
                    }
                }
            }
            return allOk;
        }
        /// <summary>
        /// Create timeseries poco to create this node in CDF
        /// </summary>
        /// <param name="variable">Variable to be converted</param>
        /// <returns>Complete timeseries write poco</returns>
        private TimeseriesWritePoco VariableToTimeseries(BufferedVariable variable)
        {
            string externalId = UAClient.GetUniqueId(variable.Id);
            var writePoco = new TimeseriesWritePoco
            {
                Description = variable.Description,
                ExternalId = externalId,
                AssetId = nodeToAssetIds[UAClient.GetUniqueId(variable.ParentId)],
                Name = variable.DisplayName,
                LegacyName = externalId
            };
            if (variable.properties != null && variable.properties.Any())
            {
                writePoco.MetaData = variable.properties
                    .Where(prop => prop.Value != null)
                    .ToDictionary(prop => prop.DisplayName, prop => prop.Value.stringValue);
            }
            writePoco.IsStep |= variable.DataType == DataTypes.Boolean;
            return writePoco;
        }
        /// <summary>
        /// Converts BufferedNode into asset write poco.
        /// </summary>
        /// <param name="node">Node to be converted</param>
        /// <returns>Full asset write poco</returns>
        private AssetWritePoco NodeToAsset(BufferedNode node)
        {
            var writePoco = new AssetWritePoco
            {
                Description = node.Description,
                ExternalId = UAClient.GetUniqueId(node.Id),
                Name = node.DisplayName
            };
            if (node.ParentId == RootNode)
            {
                writePoco.ParentId = rootAsset;
            }
            else
            {
                writePoco.ParentExternalId = UAClient.GetUniqueId(node.ParentId);
            }
            if (node.properties != null && node.properties.Any())
            {
                writePoco.MetaData = node.properties
                    .Where(prop => prop.Value != null)
                    .ToDictionary(prop => prop.DisplayName, prop => prop.Value.stringValue);
            }
            return writePoco;
        }
        #endregion
    }
}
