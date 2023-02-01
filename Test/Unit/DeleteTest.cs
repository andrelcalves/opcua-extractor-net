﻿using Cognite.Extractor.StateStorage;
using Cognite.OpcUa;
using Cognite.OpcUa.NodeSources;
using Cognite.OpcUa.TypeCollectors;
using Cognite.OpcUa.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Test.Utils;
using Xunit;
using Xunit.Abstractions;

namespace Test.Unit
{
    class MockStateStore : IExtractionStateStore
    {
        public Dictionary<string, Dictionary<string, BaseStorableState>> States { get; } = new();

        public bool ThrowOnMissingTable { get; set; }

        public Task DeleteExtractionState(IEnumerable<IExtractionState> extractionStates, string tableName, CancellationToken token)
        {
            if (!States.TryGetValue(tableName, out var state))
            {
                if (ThrowOnMissingTable) throw new InvalidOperationException("Table does not exist!");
                States[tableName] = state = new Dictionary<string, BaseStorableState>();
            }
            foreach (var s in extractionStates)
            {
                state.Remove(s.Id);
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public Task<IEnumerable<T>> GetAllExtractionStates<T>(string tableName, CancellationToken token) where T : BaseStorableState
        {
            if (!States.TryGetValue(tableName, out var state))
            {
                if (ThrowOnMissingTable) throw new InvalidOperationException("Table does not exist!");
                States[tableName] = state = new Dictionary<string, BaseStorableState>();
            }
            return Task.FromResult(state.Values.OfType<T>());
        }

        public Task RestoreExtractionState<T, K>(IDictionary<string, K> extractionStates, string tableName, Action<K, T> restoreStorableState, CancellationToken token)
            where T : BaseStorableState
            where K : IExtractionState
        {
            if (!States.TryGetValue(tableName, out var state))
            {
                if (ThrowOnMissingTable) throw new InvalidOperationException("Table does not exist!");
                States[tableName] = state = new Dictionary<string, BaseStorableState>();
            }
            foreach (var kvp in extractionStates)
            {
                if (state.TryGetValue(kvp.Key, out var inpState))
                {
                    restoreStorableState(kvp.Value, (T)inpState);
                }
            }
            return Task.CompletedTask;
        }

        public Task RestoreExtractionState<K>(IDictionary<string, K> extractionStates, string tableName, bool initializeMissing, CancellationToken token) where K : BaseExtractionState
        {
            throw new NotImplementedException();
        }

        public Task StoreExtractionState<T, K>(IEnumerable<K> extractionStates, string tableName, Func<K, T> buildStorableState, CancellationToken token)
            where T : BaseStorableState
            where K : IExtractionState
        {
            if (!States.TryGetValue(tableName, out var state))
            {
                States[tableName] = state = new Dictionary<string, BaseStorableState>();
            }
            foreach (var s in extractionStates)
            {
                state[s.Id] = buildStorableState(s);
            }
            return Task.CompletedTask;
        }

        public Task StoreExtractionState<K>(IEnumerable<K> extractionStates, string tableName, CancellationToken token) where K : BaseExtractionState
        {
            throw new NotImplementedException();
        }
    }


    [Collection("Shared server tests")]
    public class DeleteTest
    {
        private readonly StaticServerTestFixture tester;
        public DeleteTest(ITestOutputHelper output, StaticServerTestFixture tester)
        {
            this.tester = tester ?? throw new ArgumentNullException(nameof(tester));
            tester.ResetConfig();
            tester.Init(output);
        }

        private static UANode GetObject(string id)
        {
            return new UANode(new NodeId(id), id, NodeId.Null, NodeClass.Object);
        }

        private static UAVariable GetVariable(string id)
        {
            return new UAVariable(new NodeId(id), id, NodeId.Null);
        }

        private static UAReference GetReference(string source, string target, ReferenceTypeManager manager)
        {
            return new UAReference(new NodeId("type"), true, new NodeId(source), new NodeId(target), false, false, false, manager);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestDeleteManagerNodes(bool throwOnMissing)
        {
            using var store = new MockStateStore();
            store.ThrowOnMissingTable = throwOnMissing;
            var manager = new DeletesManager(store, tester.Client, tester.Provider.GetRequiredService<ILogger<DeletesManager>>(), tester.Config);

            var result = new NodeSourceResult(
                Enumerable.Empty<UANode>(),
                Enumerable.Empty<UAVariable>(),
                new[] { GetObject("obj1"), GetObject("obj2") },
                new[] { GetVariable("var1"), GetVariable("var2") },
                Enumerable.Empty<UAReference>(), false);

            // Cannot be used for deletes, so nothing happens.
            var toDelete = await manager.GetDiffAndStoreIds(result, tester.Source.Token);
            Assert.Empty(toDelete.Objects);
            Assert.Empty(toDelete.Variables);
            Assert.Empty(toDelete.References);
            Assert.Empty(store.States);

            result = new NodeSourceResult(
                Enumerable.Empty<UANode>(),
                Enumerable.Empty<UAVariable>(),
                new[] { GetObject("obj1"), GetObject("obj2") },
                new[] { GetVariable("var1"), GetVariable("var2") },
                Enumerable.Empty<UAReference>(), true);

            // No configured tables, and no references, so nothing happens
            tester.Config.StateStorage.KnownObjectsStore = null;
            tester.Config.StateStorage.KnownVariablesStore = null;
            toDelete = await manager.GetDiffAndStoreIds(result, tester.Source.Token);
            Assert.Empty(toDelete.Objects);
            Assert.Empty(toDelete.Variables);
            Assert.Empty(toDelete.References);
            Assert.Empty(store.States);

            tester.Config.StateStorage = new StateStorageConfig();

            // This time there is a store and data, so we get some states, nothing reported as deleted yet.
            toDelete = await manager.GetDiffAndStoreIds(result, tester.Source.Token);
            Assert.Empty(toDelete.Objects);
            Assert.Empty(toDelete.Variables);
            Assert.Empty(toDelete.References);
            Assert.Equal(2, store.States.Count);
            Assert.Equal(2, store.States["known_objects"].Count);

            // Remove one each of objects and variables, so we get a removed of each
            result = new NodeSourceResult(
                Enumerable.Empty<UANode>(),
                Enumerable.Empty<UAVariable>(),
                new[] { GetObject("obj2") },
                new[] { GetVariable("var2") },
                Enumerable.Empty<UAReference>(), true);

            toDelete = await manager.GetDiffAndStoreIds(result, tester.Source.Token);
            Assert.Single(toDelete.Objects);
            Assert.Single(toDelete.Variables);
            Assert.Empty(toDelete.References);
            Assert.Equal(2, store.States.Count);
            Assert.Single(store.States["known_objects"]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestDeleteManagerReferences(bool throwOnMissing)
        {
            var refManager = new ReferenceTypeManager(tester.Config, tester.Provider.GetRequiredService<ILogger<ReferenceTypeManager>>(), tester.Client, null);
            using var store = new MockStateStore();
            store.ThrowOnMissingTable = throwOnMissing;
            var manager = new DeletesManager(store, tester.Client, tester.Provider.GetRequiredService<ILogger<DeletesManager>>(), tester.Config);

            var result = new NodeSourceResult(
                Enumerable.Empty<UANode>(),
                Enumerable.Empty<UAVariable>(),
                new[] { GetObject("obj1"), GetObject("obj2") },
                new[] { GetVariable("var1"), GetVariable("var2") },
                new[] { GetReference("obj1", "obj2", refManager), GetReference("var1", "var2", refManager)}, true);

            // No configured tables, and no references, so nothing happens
            tester.Config.StateStorage.KnownObjectsStore = null;
            tester.Config.StateStorage.KnownVariablesStore = null;
            tester.Config.StateStorage.KnownReferencesStore = null;
            var toDelete = await manager.GetDiffAndStoreIds(result, tester.Source.Token);
            Assert.Empty(toDelete.Objects);
            Assert.Empty(toDelete.Variables);
            Assert.Empty(toDelete.References);
            Assert.Empty(store.States);

            tester.Config.StateStorage = new StateStorageConfig();

            // This time there is a store and data, so we get some states, nothing reported as deleted yet.
            toDelete = await manager.GetDiffAndStoreIds(result, tester.Source.Token);
            Assert.Empty(toDelete.Objects);
            Assert.Empty(toDelete.Variables);
            Assert.Empty(toDelete.References);
            Assert.Equal(3, store.States.Count);
            Assert.Equal(2, store.States["known_references"].Count);

            // Remove one each of objects, variables, and references, so we get a removed of each
            result = new NodeSourceResult(
                Enumerable.Empty<UANode>(),
                Enumerable.Empty<UAVariable>(),
                new[] { GetObject("obj2") },
                new[] { GetVariable("var2") },
                new[] { GetReference("var1", "var2", refManager) }, true);

            toDelete = await manager.GetDiffAndStoreIds(result, tester.Source.Token);
            Assert.Single(toDelete.Objects);
            Assert.Single(toDelete.Variables);
            Assert.Single(toDelete.References);
            Assert.Equal(3, store.States.Count);
            Assert.Single(store.States["known_references"]);
        }

        private static NodeSourceResult GetTestResult(UAExtractor extractor, int count)
        {
            // Create some test data
            var root = new NodeId(1);
            var nodes = Enumerable.Range(1, count).Select(i => new UANode(new NodeId($"object{i}"), $"object{i}", root, NodeClass.Object)).ToList();
            var variables = Enumerable.Range(1, count).Select(i =>
            {
                var v = new UAVariable(new NodeId($"var{i}"), $"var{i}", root);
                if (i % 2 == 0)
                {
                    v.VariableAttributes.ReadHistory = true;
                }
                return v;
            }).ToList();

            var refManager = extractor.ReferenceTypeManager;

            var references = Enumerable.Range(1, count).Select(i => new UAReference(
                ReferenceTypeIds.Organizes, true, new NodeId($"object{i}"), new NodeId($"var{i}"), false, true, false, refManager)).ToList();

            return new NodeSourceResult(Enumerable.Empty<UANode>(), Enumerable.Empty<UAVariable>(), nodes, variables, references, true);
        }

        [Fact]
        public async Task TestNotifyDeletedNodes()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            tester.Config.Extraction.Relationships.Enabled = true;
            tester.Config.Extraction.Deletes.Enabled = true;
            using var stateStore = new MockStateStore();

            using var extractor = tester.BuildExtractor(pushers: pusher, stateStore: stateStore);
            // We need a reference to the delete manager
            var deleteManager = extractor.GetType().GetField("deletesManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(extractor) as DeletesManager;


            // Register some initial nodes
            var result = GetTestResult(extractor, 2);

            // Get the diff
            var input = await PusherInput.FromNodeSourceResult(result, deleteManager, tester.Source.Token);
            // Should be empty
            Assert.Empty(input.Deletes.Objects);
            Assert.Empty(input.Deletes.Variables);
            Assert.Empty(input.Deletes.References);

            Assert.Null(pusher.LastDeleteReq);
            // Execute the push, it should succeed and we should get nodes in the pusher.
            await extractor.PushNodes(input, pusher, true);
            Assert.NotNull(pusher.LastDeleteReq);
            Assert.Null(pusher.PendingNodes);

            Assert.Equal(2, pusher.PushedNodes.Count);
            Assert.Equal(2, pusher.PushedVariables.Count);
            Assert.Equal(2, pusher.PushedReferences.Count);

            // There should also be states in the state store
            Assert.Equal(2, stateStore.States["known_objects"].Count);
            Assert.Equal(2, stateStore.States["known_variables"].Count);
            Assert.Equal(2, stateStore.States["known_references"].Count);

            // Get some more results, this time a shorter list
            result = GetTestResult(extractor, 1);

            input = await PusherInput.FromNodeSourceResult(result, deleteManager, tester.Source.Token);
            // Now there should be one of each
            Assert.Single(input.Deletes.Objects);
            Assert.Single(input.Deletes.Variables);
            Assert.Single(input.Deletes.References);

            await extractor.PushNodes(input, pusher, false);
            Assert.NotNull(pusher.LastDeleteReq);

            Assert.Single(pusher.LastDeleteReq.Objects);
            Assert.Single(pusher.LastDeleteReq.Variables);
            Assert.Single(pusher.LastDeleteReq.References);

            // Also deleted from state store
            Assert.Single(stateStore.States["known_objects"]);
            Assert.Single(stateStore.States["known_variables"]);
            Assert.Single(stateStore.States["known_references"]);

            // Next push with same input should result in no deletes
            input = await PusherInput.FromNodeSourceResult(result, deleteManager, tester.Source.Token);

            await extractor.PushNodes(input, pusher, false);
            Assert.Empty(pusher.LastDeleteReq.Objects);
            Assert.Empty(pusher.LastDeleteReq.Variables);
            Assert.Empty(pusher.LastDeleteReq.References);
        }

        [Fact]
        public async Task TestFullRunDelete()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            tester.Config.Extraction.Deletes.Enabled = true;
            tester.Config.Extraction.RootNode = tester.Ids.Audit.Root.ToProtoNodeId(tester.Client);
            using var stateStore = new MockStateStore();

            using var extractor = tester.BuildExtractor(pushers: pusher, stateStore: stateStore);

            var addedId = tester.Server.Server.AddObject(tester.Ids.Audit.Root, "NodeToDelete");
            var addedExtId = tester.Client.GetUniqueId(addedId);

            // Run the extractor and verify that we got the node.
            await extractor.RunExtractor(true);
            Assert.True(pusher.PushedNodes.ContainsKey(addedId));

            Assert.True(stateStore.States["known_objects"].ContainsKey(addedExtId));
            Assert.Empty(pusher.LastDeleteReq.Objects);

            // Run rebrowse, we should discover the deleted node.
            tester.Server.Server.RemoveNode(addedId);
            await extractor.Rebrowse();
            Assert.False(stateStore.States["known_objects"].ContainsKey(addedExtId));
            Assert.Contains(addedExtId, pusher.LastDeleteReq.Objects);
        }
    }
}