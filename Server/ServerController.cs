﻿using Opc.Ua;
using Opc.Ua.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Server
{
    sealed public class ServerController : IDisposable
    {
        public NodeIdReference Ids => Server.Ids;
        private readonly ILogger log = Log.Logger.ForContext(typeof(ServerController));
        public TestServer Server { get; private set; }
        private IEnumerable<PredefinedSetup> setups;

        public ServerController(IEnumerable<PredefinedSetup> setups)
        {
            this.setups = setups;
        }

        public void Dispose()
        {
            log.Information("Closing server");
            Server?.Stop();
            Server?.Dispose();
        }

        public async Task Start()
        {
            ApplicationInstance app = new ApplicationInstance();
            app.ConfigSectionName = "Server.Test";
            try
            {
                await app.LoadApplicationConfiguration(Path.Join("config", "Server.Test.Config.xml"), false);
                await app.CheckApplicationInstanceCertificate(false, 0);
                Server = new TestServer(setups);
                await app.Start(Server);
                log.Information("Server started");
            }
            catch (Exception e)
            {
                log.Error(e, "Failed to start server");
            }
        }
        public void Stop()
        {
            Server.Stop();
        }

        public void PopulateArrayHistory()
        {
            Server.PopulateHistory(Server.Ids.Custom.Array, 1000, "custom", 10, (i => new int[] { i, i, i, i }));
            Server.PopulateHistory(Server.Ids.Custom.MysteryVar, 1000, "int");
        }
        public void PopulateBaseHistory()
        {
            Server.PopulateHistory(Server.Ids.Base.DoubleVar1, 1000, "double");
            Server.PopulateHistory(Server.Ids.Base.StringVar, 1000, "string");
            Server.PopulateHistory(Server.Ids.Base.IntVar, 1000, "int");
        }

        public void UpdateNode(NodeId id, object value)
        {
            Server.UpdateNode(id, value);
        }

        public async Task UpdateNodeMultiple(NodeId id, int count, Func<int, object> generator, int delayms = 50)
        {
            for (int i = 0; i < count; i++)
            {
                Server.UpdateNode(id, generator(i));
                await Task.Delay(delayms);
            }
        }

        public void TriggerEvents(int idx)
        {
            // Test emitters and properties
            Server.TriggerEvent<PropertyEvent>(Ids.Event.PropType, ObjectIds.Server, Ids.Event.Obj1, "prop " + idx, evt =>
            {
                var revt = evt as PropertyEvent;
                revt.PropertyString.Value = "str " + idx;
                revt.PropertyNum.Value = idx;
                revt.SubType.Value = "sub-type";
            });
            Server.TriggerEvent<PropertyEvent>(Ids.Event.PropType, Ids.Event.Obj1, Ids.Event.Obj1, "prop-e2 " + idx, evt =>
            {
                var revt = evt as PropertyEvent;
                revt.PropertyString.Value = "str o2 " + idx;
                revt.PropertyNum.Value = idx;
                revt.SubType.Value = "sub-type";
            });
            Server.TriggerEvent<PropertyEvent>(Ids.Event.PropType, Ids.Event.Obj2, Ids.Event.Obj1, "prop-e3 " + idx, evt =>
            {
                var revt = evt as PropertyEvent;
                revt.PropertyString.Value = "str o3 - " + idx;
                revt.PropertyNum.Value = idx;
                revt.SubType.Value = "sub-type";
            });
            // Test types
            Server.TriggerEvent<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, Ids.Event.Obj1, "basic-pass " + idx);
            Server.TriggerEvent<BasicEvent2>(Ids.Event.BasicType2, ObjectIds.Server, Ids.Event.Obj1, "basic-block " + idx);
            Server.TriggerEvent<CustomEvent>(Ids.Event.CustomType, ObjectIds.Server, Ids.Event.Obj1, "mapped " + idx, evt =>
            {
                var revt = evt as CustomEvent;
                revt.TypeProp.Value = "CustomType";
            });

            // Test sources
            Server.TriggerEvent<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, Ids.Event.Obj2, "basic-pass-2 " + idx);
            Server.TriggerEvent<BasicEvent1>(Ids.Event.BasicType1, Ids.Event.Obj1, Ids.Event.Obj2, "basic-pass-3 " + idx);
            Server.TriggerEvent<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, Ids.Event.Var1, "basic-varsource " + idx);
            Server.TriggerEvent<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, null, "basic-nosource " + idx);
            Server.TriggerEvent<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, Ids.Event.ObjExclude, "basic-excludeobj " + idx);
        }

        public void PopulateEvents()
        {
            Server.PopulateEventHistory<PropertyEvent>(Ids.Event.PropType, ObjectIds.Server, Ids.Event.Obj1, "prop", 100, 100, (evt, idx) =>
            {
                var revt = evt as PropertyEvent;
                revt.PropertyString.Value = "str " + idx;
                revt.PropertyNum.Value = idx;
                revt.SubType.Value = "sub-type";
            });
            Server.PopulateEventHistory<PropertyEvent>(Ids.Event.PropType, Ids.Event.Obj1, Ids.Event.Obj1, "prop-e2", 100, 100, (evt, idx) =>
            {
                var revt = evt as PropertyEvent;
                revt.PropertyString.Value = "str o2 " + idx;
                revt.PropertyNum.Value = idx;
                revt.SubType.Value = "sub-type";
            });
            // Test types
            Server.PopulateEventHistory<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, Ids.Event.Obj1, "basic-pass", 100, 100);
            Server.PopulateEventHistory<BasicEvent2>(Ids.Event.BasicType2, ObjectIds.Server, Ids.Event.Obj1, "basic-block", 100, 100);
            Server.PopulateEventHistory<CustomEvent>(Ids.Event.CustomType, ObjectIds.Server, Ids.Event.Obj1, "mapped", 100, 100, (evt, idx) =>
            {
                var revt = evt as CustomEvent;
                revt.TypeProp.Value = "CustomType";
            });

            // Test sources
            Server.PopulateEventHistory<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, Ids.Event.Obj2, "basic-pass-2", 100, 100);
            Server.PopulateEventHistory<BasicEvent1>(Ids.Event.BasicType1, Ids.Event.Obj1, Ids.Event.Obj2, "basic-pass-3", 100, 100);
            Server.PopulateEventHistory<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, Ids.Event.Var1, "basic-varsource", 100, 100);
            Server.PopulateEventHistory<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, null, "basic-nosource", 100, 100);
            Server.PopulateEventHistory<BasicEvent1>(Ids.Event.BasicType1, ObjectIds.Server, Ids.Event.ObjExclude, "basic-excludeobj", 100, 100);
        }

        public void DirectGrowth(int idx = 0)
        {
            Server.AddObject(Ids.Audit.DirectAdd, "AddObj " + idx, true);
            Server.AddVariable(Ids.Audit.DirectAdd, "AddVar " + idx, DataTypes.Double, true);
        }
        public void ReferenceGrowth(int idx = 0)
        {
            var objId = Server.AddObject(Ids.Audit.ExcludeObj, "AddObj " + idx, true);
            var varId = Server.AddVariable(Ids.Audit.ExcludeObj, "AddVar " + idx, DataTypes.Double, true);
            Server.AddReference(objId, Ids.Audit.RefAdd, ReferenceTypeIds.HasComponent, true);
            Server.AddReference(varId, Ids.Audit.RefAdd, ReferenceTypeIds.HasComponent, true);
        }
        public void ModifyCustomServer()
        {
            Server.MutateNode(Ids.Custom.Root, root =>
            {
                root.Description = new LocalizedText("custom root description");
                root.DisplayName = new LocalizedText("CustomRoot updated");
            });
            Server.MutateNode(Ids.Custom.StringyVar, node =>
            {
                node.Description = new LocalizedText("Stringy var description");
                node.DisplayName = new LocalizedText("StringyVar updated");
            });
            Server.ReContextualize(Ids.Custom.Obj2, Ids.Custom.Root, Ids.Custom.Obj1, ReferenceTypeIds.Organizes);
            Server.ReContextualize(Ids.Custom.StringyVar, Ids.Custom.Root, Ids.Custom.Obj1, ReferenceTypeIds.HasComponent);

            Server.AddProperty<string>(Ids.Custom.StringyVar, "NewProp", DataTypeIds.String, "New prop value");
            Server.AddProperty<string>(Ids.Custom.Obj1, "NewAssetProp", DataTypeIds.String, "New asset prop value");

            Server.MutateNode(Ids.Custom.RangeProp, node =>
            {
                var prop = node as PropertyState;
                if (prop == null) return;
                prop.Value = new Opc.Ua.Range(200, 0);
            });
            Server.MutateNode(Ids.Custom.ObjProp, node =>
            {
                var prop = node as PropertyState;
                if (prop == null) return;
                prop.Value = 4321L;
            });
            Server.MutateNode(Ids.Custom.EUProp, node =>
            {
                var prop = node as PropertyState;
                if (prop == null) return;
                prop.DisplayName = new LocalizedText("EngineeringUnits updated");
            });
            Server.MutateNode(Ids.Custom.ObjProp2, node =>
            {
                var prop = node as PropertyState;
                if (prop == null) return;
                prop.DisplayName = new LocalizedText("StringProp updated");
            });
        }
    }
}