﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognite.OpcUa;
using Xunit;

namespace Test
{
    [CollectionDefinition("Client_tests", DisableParallelization = true)]
    public class UAClientTests
    {
        [Trait("Category", "basicserver")]
        [Trait("Tests", "Mapping")]
        [Fact]
        public async Task TestBasicMapping()
        {
            var fullConfig = Common.BuildConfig("basic", 0);
            Logger.Startup(fullConfig.LoggerConfig);
            int totalDps = 0;
            TestPusher pusher = new TestPusher(new Dictionary<string, Action<List<BufferedNode>, List<BufferedVariable>, List<BufferedVariable>>>
            {
                { "afterdata", (assetList, tsList, histTsList) =>
                {
                    Assert.Single(assetList);
                    Assert.Single(tsList);
                    Assert.Equal(2, histTsList.Count);
                } },
                { "afterProperties", (assetList, tsList, histTsList) =>
                {
                    Assert.NotNull(histTsList.First().properties);
                    Assert.Equal(2, histTsList.First().properties.Count);
                    Assert.Equal(2, assetList.First().properties.Count);
                } },
                { "afterSynchronize", (assetList, tsList, histTsList) =>
                {
                    bool gotDatapoints = false;
                    for (int i = 0; i < 20; i++)
                    {
                        if (totalDps > 0)
                        {
                            gotDatapoints = true;
                            break;
                        }
                        Thread.Sleep(1000);
                    }
                    Assert.True(gotDatapoints, "Expected some datapoints");
                    gotDatapoints = false;
                    int lastDps = totalDps;
                                            for (int i = 0; i < 20; i++)
                    {
                        if (totalDps > lastDps)
                        {
                            gotDatapoints = true;
                            break;
                        }
                        Thread.Sleep(1000);
                    }
                    Assert.True(gotDatapoints, "Expected dp count to be increasing");
                } }
            }, (dpList) => totalDps += dpList.Count);
            UAClient client = new UAClient(fullConfig);
            Extractor extractor = new Extractor(fullConfig, pusher, client);
            using (var source = new CancellationTokenSource())
            {
                var runTask = extractor.RunExtractor(source.Token);
                pusher.EndCB = () => { Thread.Sleep(3000); source.Cancel(); };
                try
                {
                    await runTask;
                }
                catch (Exception e)
                {
                    if (!Common.TestRunResult(e)) throw;
                }
                extractor.Close();
            }
        }
        [Trait("Category", "basicserver")]
        [Trait("Tests", "Buffer")]
        [Fact]
        public async Task TestBufferReadWrite()
        {
            var fullConfig = Common.BuildConfig("basic", 1);
            Logger.Startup(fullConfig.LoggerConfig);
            File.Create(fullConfig.CogniteConfig.BufferFile).Close();
            int dpRuns = 0;
            int totalStored = 0;
            using (var source = new CancellationTokenSource())
            using (var quitEvent = new ManualResetEvent(false))
            {
                TestPusher pusher = new TestPusher(null, (dpList) =>
                {
                    dpRuns++;
                    if (dpRuns < 5)
                    {
                        totalStored += dpList.Count;
                        Utils.WriteBufferToFile(dpList, fullConfig.CogniteConfig, source.Token);
                    }
                    else if (dpRuns == 5)
                    {
                        Logger.LogInfo("Read from file...");
                        var queue = new ConcurrentQueue<BufferedDataPoint>();
                        Utils.ReadBufferFromFile(queue, fullConfig.CogniteConfig, source.Token);
                        Assert.Equal(totalStored, queue.Count);
                        quitEvent.Set();
                    }
                });
                UAClient client = new UAClient(fullConfig);
                Extractor extractor = new Extractor(fullConfig, pusher, client);
                var runTask = extractor.RunExtractor(source.Token);
                Assert.True(quitEvent.WaitOne(20000), "Timeout");
                source.Cancel();
                try
                {
                    await runTask;
                }
                catch (Exception e)
                {
                    if (!Common.TestRunResult(e)) throw;
                }
                extractor.Close();
            }
            Assert.Equal(0, new FileInfo(fullConfig.CogniteConfig.BufferFile).Length);
        }
        [Trait("Category", "fullserver")]
        [Trait("Tests", "Bulk")]
        [Fact]
        public async Task TestBulkRequests()
        {
            var fullConfig = Common.BuildConfig("full", 2);
            Logger.Startup(fullConfig.LoggerConfig);
            int totalDps = 0;
            using (var source = new CancellationTokenSource())
            using (var quitEvent = new ManualResetEvent(false))
            {
                TestPusher pusher = new TestPusher(new Dictionary<string, Action<List<BufferedNode>, List<BufferedVariable>, List<BufferedVariable>>>
                {
                    { "afterdata", (assetList, tsList, histTsList) =>
                    {
                        Assert.Equal(153, assetList.Count);
                        Assert.Equal(2001, tsList.Count);
                        Assert.Single(histTsList);
                    } },
                    { "afterSynchronize", (assetList, tsList, histTsList) =>
                    {
                        bool gotDatapoints = false;
                        for (int i = 0; i < 20; i++)
                        {
                            if (totalDps > 0)
                            {
                                gotDatapoints = true;
                                break;
                            }
                            Thread.Sleep(1000);
                        }
                        Assert.True(gotDatapoints, "Expected some datapoints");
                        gotDatapoints = false;
                        int lastDps = totalDps;
                                                for (int i = 0; i < 20; i++)
                        {
                            if (totalDps > lastDps)
                            {
                                gotDatapoints = true;
                                break;
                            }
                            Thread.Sleep(1000);
                        }
                        Assert.True(gotDatapoints, "Expected dp count to be increasing");
                        if (!source.IsCancellationRequested) quitEvent.Set();
                    } }
                }, (dpList) => totalDps += dpList.Count);
                UAClient client = new UAClient(fullConfig);
                Extractor extractor = new Extractor(fullConfig, pusher, client);
                var runTask = extractor.RunExtractor(source.Token);
                Assert.True(quitEvent.WaitOne(30000), "Timeout");
                source.Cancel();
                try
                {
                    await runTask;
                }
                catch (Exception e)
                {
                    if (!Common.TestRunResult(e)) throw;
                }
                extractor.Close();
            }
        }
    }
}
