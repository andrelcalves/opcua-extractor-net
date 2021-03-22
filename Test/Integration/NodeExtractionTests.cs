﻿using Cognite.Extractor.Utils;
using Cognite.OpcUa;
using Cognite.OpcUa.Pushers;
using Cognite.OpcUa.Types;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Test.Utils;
using Xunit;
using Xunit.Abstractions;

namespace Test.Integration
{
    public sealed class NodeExtractionTestFixture : BaseExtractorTestFixture
    {
        public NodeExtractionTestFixture() : base(63200)
        {
            Config.History.Enabled = false;
        }
        public (CDFMockHandler, CDFPusher) GetCDFPusher()
        {
            var handler = new CDFMockHandler("test", CDFMockHandler.MockMode.None);
            handler.StoreDatapoints = true;
            CommonTestUtils.AddDummyProvider(handler, Services);
            Services.AddCogniteClient("appid", true, true, false);
            var provider = Services.BuildServiceProvider();
            var pusher = Config.Cognite.ToPusher(provider) as CDFPusher;
            return (handler, pusher);
        }
    }

    // Tests for various configurations for extracting nodes and pushing to dummy pusher
    public class NodeExtractionTests : MakeConsoleWork, IClassFixture<NodeExtractionTestFixture>
    {
        NodeExtractionTestFixture tester;
        public NodeExtractionTests(ITestOutputHelper output, NodeExtractionTestFixture tester) : base(output)
        {
            this.tester = tester;
        }
        #region datatypeconfig
        [Fact]
        public async Task TestNoDataTypeConfig()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            await extractor.RunExtractor(true);

            Assert.Equal(3, pusher.PushedNodes.Count);
            var node = pusher.PushedNodes[ids.Root];
            Assert.Equal("CustomRoot", node.DisplayName);
            Assert.True(node.ParentId == null || node.ParentId.IsNullNodeId);
            Assert.True(node.Properties == null || !node.Properties.Any());
            Assert.False(node is UAVariable);

            node = pusher.PushedNodes[ids.Obj1];
            Assert.Equal("ChildObject", node.DisplayName);
            Assert.Equal(ids.Root, node.ParentId);
            Assert.True(node.Properties == null || !node.Properties.Any());
            Assert.False(node is UAVariable);

            node = pusher.PushedNodes[ids.Obj2];
            Assert.Equal("ChildObject2", node.DisplayName);
            Assert.Equal(ids.Root, node.ParentId);
            Assert.Equal(2, node.Properties.Count());
            var prop = node.Properties.First(prop => prop.DisplayName == "NumericProp") as UAVariable;
            Assert.Equal(DataTypeIds.Int64, prop.DataType.Raw);
            Assert.Equal("1234", prop.Value.StringValue);
            prop = node.Properties.First(prop => prop.DisplayName == "StringProp") as UAVariable;
            Assert.Equal(DataTypeIds.String, prop.DataType.Raw);
            Assert.Equal("String prop value", prop.Value.StringValue);
            Assert.False(node is UAVariable);

            // No normal datatypes here
            Assert.Empty(pusher.PushedVariables);
        }

        [Fact]
        public async Task TestAllowStringTypes()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            dataTypes.AllowStringVariables = true;
            await extractor.RunExtractor(true);

            Assert.Equal(3, pusher.PushedNodes.Count);
            Assert.Equal(6, pusher.PushedVariables.Count);
            Assert.All(pusher.PushedVariables.Values, variable => Assert.True(variable.DataType.IsString));

            var vnode = pusher.PushedVariables[(ids.MysteryVar, -1)];
            Assert.Equal("MysteryVar", vnode.DisplayName);
            Assert.Equal(ids.MysteryType, vnode.DataType.Raw);
            Assert.Equal(ids.Root, vnode.ParentId);
            Assert.Equal(2, vnode.Properties.Count());
            var prop = vnode.Properties.First(prop => prop.DisplayName == "EngineeringUnits") as UAVariable;
            Assert.Equal(DataTypeIds.EUInformation, prop.DataType.Raw);
            Assert.Equal("°C: degree Celsius", prop.Value.StringValue);
            prop = vnode.Properties.First(prop => prop.DisplayName == "EURange") as UAVariable;
            Assert.Equal(DataTypeIds.Range, prop.DataType.Raw);
            Assert.Equal("(0, 100)", prop.Value.StringValue);

            Assert.All(pusher.PushedVariables.Values.Where(variable => variable.DisplayName != "MysteryVar"
                && variable.DisplayName != "NumberVar"),
                variable => Assert.True(variable.Properties == null || !variable.Properties.Any()));

            dataTypes.AllowStringVariables = false;
        }
        [Fact]
        public async Task TestAllowSmallArrays()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            dataTypes.AllowStringVariables = true;
            dataTypes.MaxArraySize = 2;
            await extractor.RunExtractor(true);

            Assert.Equal(4, pusher.PushedNodes.Count);
            Assert.Equal(8, pusher.PushedVariables.Count);
            Assert.All(pusher.PushedVariables.Values, variable => Assert.True(variable.DataType.IsString));

            var node = pusher.PushedNodes[ids.StringArray];
            Assert.Equal("Variable StringArray", node.DisplayName);
            var arr = Assert.IsType<UAVariable>(node);
            Assert.True(arr is UAVariable);
            Assert.True(arr.Properties == null || !arr.Properties.Any());
            Assert.True(arr.IsArray);
            Assert.Equal(2, arr.ArrayDimensions[0]);
            Assert.Equal(2, arr.ArrayChildren.Count());
            Assert.Equal(DataTypeIds.String, arr.DataType.Raw);
            Assert.Equal(-1, arr.Index);

            var vnode = pusher.PushedVariables[(ids.StringArray, 0)];
            Assert.True(vnode.IsArray);
            Assert.Equal(0, vnode.Index);
            Assert.Contains(vnode, arr.ArrayChildren);
            Assert.Equal(2, vnode.ArrayDimensions[0]);
            Assert.Equal(arr, vnode.ArrayParent);
            Assert.Equal(DataTypeIds.String, vnode.DataType.Raw);
            vnode = pusher.PushedVariables[(ids.StringArray, 1)];
            Assert.Contains(vnode, arr.ArrayChildren);
            Assert.Equal(1, vnode.Index);
            Assert.False(pusher.PushedVariables.ContainsKey((ids.StringArray, 2)));

            dataTypes.AllowStringVariables = false;
            dataTypes.MaxArraySize = 0;
        }
        [Fact]
        public async Task TestAllowLargerArrays()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            dataTypes.AllowStringVariables = true;
            dataTypes.MaxArraySize = -1;
            await extractor.RunExtractor(true);

            Assert.Equal(6, pusher.PushedNodes.Count);
            Assert.Equal(16, pusher.PushedVariables.Count);
            Assert.All(pusher.PushedVariables.Values, variable =>
                Assert.True(variable.DataType.IsString
                    || variable.DisplayName.StartsWith("Variable Array", StringComparison.InvariantCulture)));

            var node = pusher.PushedNodes[ids.Array];
            Assert.Equal("Variable Array", node.DisplayName);
            var arr = Assert.IsType<UAVariable>(node);
            Assert.True(arr is UAVariable);
            Assert.Equal(2, arr.Properties.Count());
            var prop = arr.Properties.First(prop => prop.DisplayName == "EngineeringUnits") as UAVariable;
            Assert.Equal(DataTypeIds.EUInformation, prop.DataType.Raw);
            Assert.Equal("°C: degree Celsius", prop.Value.StringValue);
            prop = arr.Properties.First(prop => prop.DisplayName == "EURange") as UAVariable;
            Assert.Equal(DataTypeIds.Range, prop.DataType.Raw);
            Assert.Equal("(0, 100)", prop.Value.StringValue);
            Assert.True(arr.IsArray);
            Assert.Equal(4, arr.ArrayDimensions[0]);
            Assert.Equal(4, arr.ArrayChildren.Count());
            Assert.Equal(DataTypeIds.Double, arr.DataType.Raw);

            dataTypes.AllowStringVariables = false;
            dataTypes.MaxArraySize = 0;
        }
        [Fact]
        public async Task TestAutoIdentifyTypes()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            dataTypes.AllowStringVariables = true;
            dataTypes.MaxArraySize = -1;
            dataTypes.AutoIdentifyTypes = true;
            await extractor.RunExtractor(true);

            Assert.Equal(6, pusher.PushedNodes.Count);
            Assert.Equal(16, pusher.PushedVariables.Count);
            Assert.Equal(6, pusher.PushedVariables.Count(vb => vb.Value.DataType.EnumValues != null));
            Assert.Equal(5, pusher.PushedVariables.Count(vb => vb.Value.DataType.IsString));

            var enumv = pusher.PushedVariables[(ids.EnumVar1, -1)];
            Assert.Equal(ids.EnumType1, enumv.DataType.Raw);
            Assert.Equal(3, enumv.DataType.EnumValues.Count);
            var dp = enumv.DataType.ToDataPoint(extractor, 1, DateTime.UtcNow, "test");
            Assert.Equal(1, dp.DoubleValue);

            var node = pusher.PushedNodes[ids.EnumVar3];
            var enumArr = Assert.IsType<UAVariable>(node);
            Assert.Equal(ids.EnumType2, enumArr.DataType.Raw);
            Assert.Equal(2, enumArr.DataType.EnumValues.Count);
            dp = enumArr.DataType.ToDataPoint(extractor, 123, DateTime.UtcNow, "test");
            Assert.Equal(123, dp.DoubleValue);

            var vnode = pusher.PushedVariables[(ids.MysteryVar, -1)];
            Assert.False(vnode.DataType.IsString);

            var nnode = pusher.PushedVariables[(ids.NumberVar, -1)];
            Assert.Equal(4, nnode.GetAllProperties().Count());

            dataTypes.AllowStringVariables = false;
            dataTypes.MaxArraySize = 0;
            dataTypes.AutoIdentifyTypes = false;
        }

        [Fact]
        public async Task TestEnumsAsStrings()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            dataTypes.AllowStringVariables = true;
            dataTypes.MaxArraySize = -1;
            dataTypes.AutoIdentifyTypes = true;
            dataTypes.EnumsAsStrings = true;
            await extractor.RunExtractor(true);

            Assert.Equal(6, pusher.PushedNodes.Count);
            Assert.Equal(16, pusher.PushedVariables.Count);
            Assert.Equal(6, pusher.PushedVariables.Count(vb => vb.Value.DataType.EnumValues != null));
            Assert.Equal(11, pusher.PushedVariables.Count(vb => vb.Value.DataType.IsString));

            var enumv = pusher.PushedVariables[(ids.EnumVar1, -1)];
            Assert.Equal(ids.EnumType1, enumv.DataType.Raw);
            Assert.Equal(3, enumv.DataType.EnumValues.Count);
            var dp = enumv.DataType.ToDataPoint(extractor, 1, DateTime.UtcNow, "test");
            Assert.Equal("Enum2", dp.StringValue);

            var node = pusher.PushedNodes[ids.EnumVar3];
            var enumArr = Assert.IsType<UAVariable>(node);
            Assert.Equal(ids.EnumType2, enumArr.DataType.Raw);
            Assert.Equal(2, enumArr.DataType.EnumValues.Count);
            dp = enumArr.DataType.ToDataPoint(extractor, 123, DateTime.UtcNow, "test");
            Assert.Equal("VEnum2", dp.StringValue);

            var vnode = pusher.PushedVariables[(ids.MysteryVar, -1)];
            Assert.False(vnode.DataType.IsString);

            dataTypes.AllowStringVariables = false;
            dataTypes.MaxArraySize = 0;
            dataTypes.AutoIdentifyTypes = false;
            dataTypes.EnumsAsStrings = false;
        }
        [Fact]
        public async Task TestIgnoreDataType()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            dataTypes.AllowStringVariables = true;
            dataTypes.MaxArraySize = -1;
            dataTypes.AutoIdentifyTypes = true;
            dataTypes.IgnoreDataTypes = new List<ProtoNodeId> { CommonTestUtils.ToProtoNodeId(ids.IgnoreType, tester.Client) };
            await extractor.RunExtractor(true);

            Assert.Equal(6, pusher.PushedNodes.Count);
            Assert.Equal(15, pusher.PushedVariables.Count);

            Assert.False(pusher.PushedVariables.ContainsKey((ids.IgnoreVar, -1)));

            dataTypes.AllowStringVariables = false;
            dataTypes.MaxArraySize = 0;
            dataTypes.AutoIdentifyTypes = false;
            dataTypes.IgnoreDataTypes = null;
        }
        [Fact]
        public async Task TestCustomDataType()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            dataTypes.AllowStringVariables = true;
            dataTypes.MaxArraySize = -1;
            dataTypes.AutoIdentifyTypes = true;
            dataTypes.CustomNumericTypes = new List<ProtoDataType> {
                new ProtoDataType
                {
                    IsStep = true,
                    NodeId = CommonTestUtils.ToProtoNodeId(ids.NumberType, tester.Client)
                }
            };
            await extractor.RunExtractor(true);

            Assert.Equal(6, pusher.PushedNodes.Count);
            Assert.Equal(16, pusher.PushedVariables.Count);

            var node = pusher.PushedVariables[(ids.NumberVar, -1)];
            Assert.True(node.DataType.IsStep);
            Assert.False(node.DataType.IsString);
            Assert.Equal(ids.NumberType, node.DataType.Raw);

            dataTypes.AllowStringVariables = false;
            dataTypes.MaxArraySize = 0;
            dataTypes.AutoIdentifyTypes = false;
            dataTypes.CustomNumericTypes = null;
        }
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestNullDataType(bool nullAsNumeric)
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Wrong;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            dataTypes.AllowStringVariables = true;
            dataTypes.AutoIdentifyTypes = true;
            dataTypes.NullAsNumeric = nullAsNumeric;
            await extractor.RunExtractor(true);

            Assert.Single(pusher.PushedNodes);
            Assert.Single(pusher.PushedVariables);

            var node = pusher.PushedVariables.Values.First();
            Assert.Equal("NullType", node.DisplayName);
            Assert.Equal(NodeId.Null, node.DataType.Raw);
            Assert.NotEqual(nullAsNumeric, node.DataType.IsString);

            dataTypes.AllowStringVariables = false;
            dataTypes.AutoIdentifyTypes = false;
            dataTypes.NullAsNumeric = false;
        }
        [Fact]
        public async Task TestUnknownAsScalar()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Wrong;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var dataTypes = tester.Config.Extraction.DataTypes;

            dataTypes.AllowStringVariables = true;
            dataTypes.AutoIdentifyTypes = true;
            dataTypes.UnknownAsScalar = true;
            dataTypes.MaxArraySize = -1;
            await extractor.RunExtractor(true);

            Assert.Equal(3, pusher.PushedNodes.Count);
            Assert.Equal(10, pusher.PushedVariables.Count);

            var vnode = pusher.PushedVariables[(ids.RankImpreciseNoDim, -1)];
            Assert.Equal("RankImpreciseNoDim", vnode.DisplayName);

            var node = pusher.PushedNodes[ids.RankImprecise];
            var arr = Assert.IsType<UAVariable>(node);
            Assert.Equal(4, arr.ArrayDimensions[0]);

            dataTypes.AllowStringVariables = false;
            dataTypes.AutoIdentifyTypes = false;
            dataTypes.UnknownAsScalar = false;
            dataTypes.MaxArraySize = 0;
        }

        #endregion

        #region structureconfig
        [Fact]
        public async Task TestIgnoreName()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var extraction = tester.Config.Extraction;

            extraction.IgnoreName = new[] { "Child", "ChildObject", "Variable", "IgnoreVar", "Variable Array", "EURange", "NumericProp" };
            extraction.DataTypes.AllowStringVariables = true;
            extraction.DataTypes.MaxArraySize = -1;

            await extractor.RunExtractor(true);

            Assert.Equal(4, pusher.PushedNodes.Count);
            Assert.Equal(11, pusher.PushedVariables.Count);
            Assert.True(pusher.PushedNodes.ContainsKey(ids.StringArray));
            Assert.False(pusher.PushedNodes.ContainsKey(ids.Array));
            Assert.True(pusher.PushedVariables.ContainsKey((ids.StringArray, 1)));
            Assert.True(pusher.PushedVariables.ContainsKey((ids.MysteryVar, -1)));
            Assert.False(pusher.PushedVariables.ContainsKey((ids.IgnoreVar, -1)));
            Assert.False(pusher.PushedVariables.ContainsKey((ids.Array, 1)));

            var vnode = pusher.PushedVariables[(ids.MysteryVar, -1)];
            Assert.Single(vnode.Properties);
            Assert.DoesNotContain(vnode.Properties, prop => prop.DisplayName == "EURange");

            var node = pusher.PushedNodes[ids.Obj2];
            Assert.Single(node.Properties);

            extraction.IgnoreName = null;
            extraction.DataTypes.AllowStringVariables = false;
            extraction.DataTypes.MaxArraySize = 0;
        }
        [Fact]
        public async Task TestIgnoreNamePrefix()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);
            var extraction = tester.Config.Extraction;

            extraction.IgnoreNamePrefix = new[] { "Variable", "Ignore", "EUR", "Numeric" };
            extraction.DataTypes.AllowStringVariables = true;
            extraction.DataTypes.MaxArraySize = -1;

            await extractor.RunExtractor(true);

            Assert.Equal(4, pusher.PushedNodes.Count);
            Assert.Equal(9, pusher.PushedVariables.Count);
            Assert.False(pusher.PushedNodes.ContainsKey(ids.StringArray));
            Assert.False(pusher.PushedNodes.ContainsKey(ids.Array));
            Assert.False(pusher.PushedVariables.ContainsKey((ids.StringArray, 1)));
            Assert.True(pusher.PushedVariables.ContainsKey((ids.MysteryVar, -1)));
            Assert.False(pusher.PushedVariables.ContainsKey((ids.IgnoreVar, -1)));
            Assert.False(pusher.PushedVariables.ContainsKey((ids.Array, 1)));

            var vnode = pusher.PushedVariables[(ids.MysteryVar, -1)];
            Assert.Single(vnode.Properties);
            Assert.DoesNotContain(vnode.Properties, prop => prop.DisplayName == "EURange");

            var node = pusher.PushedNodes[ids.Obj2];
            Assert.Single(node.Properties);

            extraction.IgnoreNamePrefix = null;
            extraction.DataTypes.AllowStringVariables = false;
            extraction.DataTypes.MaxArraySize = 0;
        }
        [Fact]
        public async Task TestPropertyNameFilter()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            var extraction = tester.Config.Extraction;
            extraction.PropertyNameFilter = "ble Str|ble Arr|r[0-9]$";
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);

            extraction.DataTypes.AllowStringVariables = true;
            extraction.DataTypes.MaxArraySize = -1;
            extraction.DataTypes.AutoIdentifyTypes = true;

            await extractor.RunExtractor(true);

            Assert.Equal(3, pusher.PushedNodes.Count);
            Assert.Equal(4, pusher.PushedVariables.Count);
            Assert.False(pusher.PushedNodes.ContainsKey(ids.StringArray));
            Assert.False(pusher.PushedNodes.ContainsKey(ids.Array));
            Assert.False(pusher.PushedVariables.ContainsKey((ids.StringArray, 1)));
            Assert.True(pusher.PushedVariables.ContainsKey((ids.MysteryVar, -1)));
            Assert.False(pusher.PushedVariables.ContainsKey((ids.Array, 1)));

            var node = pusher.PushedNodes[ids.Root];
            Assert.Equal(5, node.Properties.Count());
            var prop = node.Properties.First(prop => prop.DisplayName == "Variable StringArray") as UAVariable;
            Assert.Equal("[test1, test2]", prop.Value.StringValue);
            prop = node.Properties.First(prop => prop.DisplayName == "Variable Array") as UAVariable;
            Assert.Equal("[0, 0, 0, 0]", prop.Value.StringValue);
            prop = node.Properties.First(prop => prop.DisplayName == "EnumVar1") as UAVariable;
            Assert.Equal("Enum2", prop.Value.StringValue);
            prop = node.Properties.First(prop => prop.DisplayName == "EnumVar2") as UAVariable;
            Assert.Equal("VEnum2", prop.Value.StringValue);
            prop = node.Properties.First(prop => prop.DisplayName == "EnumVar3") as UAVariable;
            Assert.Equal("[VEnum2, VEnum2, VEnum1, VEnum2]", prop.Value.StringValue);

            extraction.PropertyNameFilter = null;
            extraction.DataTypes.AllowStringVariables = false;
            extraction.DataTypes.MaxArraySize = 0;
            extraction.DataTypes.AutoIdentifyTypes = false;
        }
        [Fact]
        public async Task TestPropertyIdFilter()
        {
            using var pusher = new DummyPusher(new DummyPusherConfig());
            var extraction = tester.Config.Extraction;
            extraction.PropertyIdFilter = "enum";
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);

            extraction.DataTypes.AllowStringVariables = true;
            extraction.DataTypes.MaxArraySize = -1;
            extraction.DataTypes.AutoIdentifyTypes = true;

            await extractor.RunExtractor(true);

            Assert.Equal(6, pusher.PushedNodes.Count);
            Assert.Equal(15, pusher.PushedVariables.Count);
            Assert.False(pusher.PushedVariables.ContainsKey((ids.EnumVar2, -1)));

            var node = pusher.PushedNodes[ids.Root];
            Assert.Single(node.Properties);
            var prop = node.Properties.First(prop => prop.DisplayName == "EnumVar2") as UAVariable;
            Assert.Equal("VEnum2", prop.Value.StringValue);

            extraction.PropertyIdFilter = null;
            extraction.DataTypes.AllowStringVariables = false;
            extraction.DataTypes.MaxArraySize = 0;
            extraction.DataTypes.AutoIdentifyTypes = false;
        }
        [Fact]
        public async Task TestMultipleSourceNodes()
        {
            using var pusher = new DummyPusher(new DummyPusherConfig());
            var extraction = tester.Config.Extraction;
            using var extractor = tester.BuildExtractor(true, null, pusher);

            // Duplicates should be handled
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Custom.Root, tester.Client);
            tester.Config.Extraction.RootNodes = new[]
            {
                CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Custom.Root, tester.Client),
                CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Base.Root, tester.Client),
            };

            extraction.DataTypes.AllowStringVariables = true;
            extraction.DataTypes.MaxArraySize = -1;
            extraction.DataTypes.AutoIdentifyTypes = true;

            await extractor.RunExtractor(true);

            extraction.DataTypes.AllowStringVariables = false;
            extraction.DataTypes.MaxArraySize = 0;
            extraction.DataTypes.AutoIdentifyTypes = false;
            extraction.RootNodes = null;

            Assert.Equal(7, pusher.PushedNodes.Count);
            Assert.Equal(21, pusher.PushedVariables.Count);
        }
        #endregion

        #region custommetadata
        [Fact]
        public async Task TestExtraMetadata()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            var extraction = tester.Config.Extraction;
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var ids = tester.Server.Ids.Custom;
            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ids.Root, tester.Client);

            extraction.DataTypes.AllowStringVariables = true;
            extraction.DataTypes.MaxArraySize = -1;
            extraction.DataTypes.AutoIdentifyTypes = true;
            extraction.DataTypes.DataTypeMetadata = true;
            extraction.NodeTypes.Metadata = true;

            await extractor.RunExtractor(true);

            Assert.Equal(6, pusher.PushedNodes.Count);
            Assert.Equal(16, pusher.PushedVariables.Count);

            var node = pusher.PushedNodes[ids.Root];
            var metadata = extractor.GetExtraMetadata(node);
            Assert.Single(metadata);
            Assert.Equal("BaseObjectType", metadata["TypeDefinition"]);

            node = pusher.PushedNodes[ids.Array];
            metadata = extractor.GetExtraMetadata(node);
            Assert.Equal(2, metadata.Count);
            Assert.Equal("BaseDataVariableType", metadata["TypeDefinition"]);
            Assert.Equal("Double", metadata["dataType"]);

            node = pusher.PushedNodes[ids.EnumVar3];
            metadata = extractor.GetExtraMetadata(node);
            Assert.Equal(4, metadata.Count);
            Assert.Equal("BaseDataVariableType", metadata["TypeDefinition"]);
            Assert.Equal("CustomEnumType2", metadata["dataType"]);
            Assert.Equal("VEnum1", metadata["321"]);
            Assert.Equal("VEnum2", metadata["123"]);

            node = pusher.PushedVariables[(ids.EnumVar3, 1)];
            metadata = extractor.GetExtraMetadata(node);
            Assert.Equal(4, metadata.Count);
            Assert.Equal("BaseDataVariableType", metadata["TypeDefinition"]);
            Assert.Equal("CustomEnumType2", metadata["dataType"]);
            Assert.Equal("VEnum1", metadata["321"]);
            Assert.Equal("VEnum2", metadata["123"]);

            node = pusher.PushedVariables[(ids.MysteryVar, -1)];
            metadata = extractor.GetExtraMetadata(node);
            Assert.Equal(2, metadata.Count);
            Assert.Equal("BaseDataVariableType", metadata["TypeDefinition"]);
            Assert.Equal("MysteryType", metadata["dataType"]);

            extraction.DataTypes.AllowStringVariables = false;
            extraction.DataTypes.MaxArraySize = 0;
            extraction.DataTypes.AutoIdentifyTypes = false;
            extraction.DataTypes.DataTypeMetadata = false;
            extraction.NodeTypes.Metadata = false;
        }
        #endregion

        #region references
        private async Task RunReferenceExtraction(UAExtractor extractor)
        {
            var dataTypes = tester.Config.Extraction.DataTypes;

            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Custom.Root, tester.Client);
            dataTypes.AllowStringVariables = true;
            dataTypes.MaxArraySize = 4;
            dataTypes.AutoIdentifyTypes = true;
            dataTypes.IgnoreDataTypes = new[]
            {
                CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Custom.IgnoreType, tester.Client)
            };

            await extractor.RunExtractor(true);

            dataTypes.AllowStringVariables = false;
            dataTypes.MaxArraySize = 0;
            dataTypes.AutoIdentifyTypes = false;
            dataTypes.IgnoreDataTypes = null;
        }
        [Fact]
        public async Task TestBasicReferences()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            tester.Config.Extraction.Relationships.Enabled = true;

            using var extractor = tester.BuildExtractor(true, null, pusher);
            await RunReferenceExtraction(extractor);

            Assert.Equal(8, pusher.PushedReferences.Count);
            Assert.Equal(4, pusher.PushedReferences.Count(rel => rel.IsForward));

            Assert.All(pusher.PushedReferences, rel =>
            {
                Assert.NotNull(rel.Source);
                Assert.NotNull(rel.Target);
                Assert.False(rel.Source.Id.IsNullNodeId);
                Assert.False(rel.Target.Id.IsNullNodeId);
                Assert.NotNull(rel.Type);
                Assert.NotNull(rel.Type.Id);
                Assert.True(rel.Type.HasName);
                Assert.Contains(pusher.PushedReferences, orel => orel.Source.Id == rel.Target.Id
                    && orel.Target.Id == rel.Source.Id && orel.IsForward == !rel.IsForward);
            });

            tester.Config.Extraction.Relationships.Enabled = false;
        }
        [Fact]
        public async Task TestHierarchicalReferences()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            tester.Config.Extraction.Relationships.Enabled = true;
            tester.Config.Extraction.Relationships.Hierarchical = true;

            using var extractor = tester.BuildExtractor(true, null, pusher);
            await RunReferenceExtraction(extractor);

            Assert.Equal(18, pusher.PushedReferences.Count);
            Assert.Equal(14, pusher.PushedReferences.Count(rel => rel.IsForward));

            Assert.All(pusher.PushedReferences, rel =>
            {
                Assert.NotNull(rel.Source);
                Assert.NotNull(rel.Target);
                Assert.False(rel.Source.Id.IsNullNodeId);
                Assert.False(rel.Target.Id.IsNullNodeId);
                Assert.NotNull(rel.Type);
                Assert.NotNull(rel.Type.Id);
                Assert.True(rel.Type.HasName);
            });

            tester.Config.Extraction.Relationships.Enabled = false;
            tester.Config.Extraction.Relationships.Hierarchical = false;
        }
        [Fact]
        public async Task TestInverseHierarchicalReferences()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            tester.Config.Extraction.Relationships.Enabled = true;
            tester.Config.Extraction.Relationships.Hierarchical = true;
            tester.Config.Extraction.Relationships.InverseHierarchical = true;
            using var extractor = tester.BuildExtractor(true, null, pusher);

            await RunReferenceExtraction(extractor);

            Assert.Equal(28, pusher.PushedReferences.Count);
            Assert.Equal(14, pusher.PushedReferences.Count(rel => rel.IsForward));
            Assert.All(pusher.PushedReferences, rel =>
            {
                Assert.NotNull(rel.Source);
                Assert.NotNull(rel.Target);
                Assert.False(rel.Source.Id.IsNullNodeId);
                Assert.False(rel.Target.Id.IsNullNodeId);
                Assert.NotNull(rel.Type);
                Assert.NotNull(rel.Type.Id);
                Assert.True(rel.Type.HasName);
                Assert.Contains(pusher.PushedReferences, orel => orel.Source.Id == rel.Target.Id
                    && orel.Target.Id == rel.Source.Id && orel.IsForward == !rel.IsForward);
            });
            tester.Config.Extraction.Relationships.Enabled = false;
            tester.Config.Extraction.Relationships.Hierarchical = false;
            tester.Config.Extraction.Relationships.InverseHierarchical = false;
        }
        #endregion

        #region lateinit
        [Fact]
        public async Task TestLateInitInitialFail()
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            tester.Config.Extraction.Relationships.Enabled = true;
            tester.Config.Extraction.Relationships.Hierarchical = true;
            tester.Config.Extraction.Relationships.InverseHierarchical = true;
            using var extractor = tester.BuildExtractor(true, null, pusher);

            pusher.NoInit = true;
            pusher.TestConnectionResult = false;

            var dataTypes = tester.Config.Extraction.DataTypes;

            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Custom.Root, tester.Client);
            dataTypes.AllowStringVariables = true;
            dataTypes.MaxArraySize = 4;
            dataTypes.AutoIdentifyTypes = true;

            var runTask = extractor.RunExtractor();
            await extractor.WaitForSubscriptions();

            Assert.Empty(pusher.PushedNodes);
            Assert.Empty(pusher.PushedVariables);
            Assert.Equal(22, pusher.PendingNodes.Count);
            Assert.Equal(32, pusher.PendingReferences.Count);

            Assert.False(pusher.Initialized);

            pusher.TestConnectionResult = true;

            await CommonTestUtils.WaitForCondition(() =>
                pusher.PushedNodes.Count == 6
                && pusher.PushedReferences.Count == 32
                && pusher.PushedVariables.Count == 16, 5, () => $"Expected to get 6 nodes, got {pusher.PushedNodes.Count}, " +
                    $"16 variables and got {pusher.PushedVariables.Count}, 32 references and got {pusher.PushedReferences.Count}");

            Assert.False(pusher.NoInit);
            Assert.True(pusher.Initialized);

            extractor.Close(false);


            await Assert.ThrowsAsync<TaskCanceledException>(async () => await runTask);

            tester.Config.Extraction.Relationships.Enabled = false;
            tester.Config.Extraction.Relationships.Hierarchical = false;
            tester.Config.Extraction.Relationships.InverseHierarchical = false;
            dataTypes.AllowStringVariables = false;
            dataTypes.MaxArraySize = 0;
            dataTypes.AutoIdentifyTypes = false;
        }
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task TestLateInitLateFail(bool failNodes, bool failReferences)
        {
            var pusher = new DummyPusher(new DummyPusherConfig());
            tester.Config.Extraction.Relationships.Enabled = true;
            tester.Config.Extraction.Relationships.Hierarchical = true;
            tester.Config.Extraction.Relationships.InverseHierarchical = true;
            using var extractor = tester.BuildExtractor(true, null, pusher);

            pusher.NoInit = false;
            pusher.TestConnectionResult = false;
            pusher.PushNodesResult = !failNodes;
            pusher.PushReferenceResult = !failReferences;

            var dataTypes = tester.Config.Extraction.DataTypes;

            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Custom.Root, tester.Client);
            dataTypes.AllowStringVariables = true;
            dataTypes.MaxArraySize = 4;
            dataTypes.AutoIdentifyTypes = true;

            var runTask = extractor.RunExtractor();
            await extractor.WaitForSubscriptions();

            if (failNodes)
            {
                Assert.Empty(pusher.PushedNodes);
                Assert.Empty(pusher.PushedVariables);
                Assert.Equal(22, pusher.PendingNodes.Count);
            }
            if (failReferences)
            {
                Assert.Empty(pusher.PushedReferences);
                Assert.Equal(32, pusher.PendingReferences.Count);
            }


            Assert.False(pusher.Initialized);

            pusher.TestConnectionResult = true;
            pusher.PushNodesResult = true;
            pusher.PushReferenceResult = true;

            await CommonTestUtils.WaitForCondition(() =>
                pusher.PushedNodes.Count == 6
                && pusher.PushedReferences.Count == 32
                && pusher.PushedVariables.Count == 16, 5, () => $"Expected to get 6 nodes, got {pusher.PushedNodes.Count}, " +
                    $"16 variables and got {pusher.PushedVariables.Count}, 32 references and got {pusher.PushedReferences.Count}");

            Assert.True(pusher.Initialized);

            extractor.Close(false);


            await Assert.ThrowsAsync<TaskCanceledException>(async () => await runTask);

            tester.Config.Extraction.Relationships.Enabled = false;
            tester.Config.Extraction.Relationships.Hierarchical = false;
            tester.Config.Extraction.Relationships.InverseHierarchical = false;
            dataTypes.AllowStringVariables = false;
            dataTypes.MaxArraySize = 0;
            dataTypes.AutoIdentifyTypes = false;
        }
        #endregion

        #region updates
        [Theory]
        [InlineData(true, true, true, true, false, false, false, false)]
        [InlineData(false, false, false, false, true, true, true, true)]
        [InlineData(true, false, true, false, true, false, true, false)]
        [InlineData(false, true, false, true, false, true, false, true)]
        [InlineData(true, true, true, true, true, true, true, true)]
        public async Task TestUpdateFields(
            bool assetName, bool variableName,
            bool assetDesc, bool variableDesc,
            bool assetContext, bool variableContext,
            bool assetMeta, bool variableMeta)
        {
            var (handler, pusher) = tester.GetCDFPusher();
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var upd = tester.Config.Extraction.Update;
            upd.Objects.Name = assetName;
            upd.Objects.Description = assetDesc;
            upd.Objects.Context = assetContext;
            upd.Objects.Metadata = assetMeta;
            upd.Variables.Name = variableName;
            upd.Variables.Description = variableDesc;
            upd.Variables.Context = variableContext;
            upd.Variables.Metadata = variableMeta;

            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Custom.Root, tester.Client);

            tester.Config.Extraction.DataTypes.AllowStringVariables = true;
            tester.Config.Extraction.DataTypes.MaxArraySize = 4;
            tester.Config.History.Enabled = false;

            var runTask = extractor.RunExtractor();

            await CommonTestUtils.WaitForCondition(() => handler.Assets.Any() && handler.Timeseries.Any(), 5);

            CommonTestUtils.VerifyStartingConditions(handler.Assets, handler.Timeseries, null, extractor, tester.Server.Ids.Custom, false);

            tester.Server.ModifyCustomServer();

            var rebrowseTask = extractor.Rebrowse();
            await Task.WhenAny(rebrowseTask, Task.Delay(10000));
            Assert.True(rebrowseTask.IsCompleted);

            CommonTestUtils.VerifyStartingConditions(handler.Assets, handler.Timeseries, upd, extractor, tester.Server.Ids.Custom, false);
            CommonTestUtils.VerifyModified(handler.Assets, handler.Timeseries, upd, extractor, tester.Server.Ids.Custom, false);

            tester.Server.ResetCustomServer();
            tester.Config.Extraction.Update = new UpdateConfig();
            tester.Config.Extraction.DataTypes.AllowStringVariables = false;
            tester.Config.Extraction.DataTypes.MaxArraySize = 0;

            await BaseExtractorTestFixture.TerminateRunTask(runTask, extractor);
        }
        [Theory]
        [InlineData(true, true, true, true, false, false, false, false)]
        [InlineData(false, false, false, false, true, true, true, true)]
        [InlineData(true, false, true, false, true, false, true, false)]
        [InlineData(false, true, false, true, false, true, false, true)]
        [InlineData(true, true, true, true, true, true, true, true)]
        public async Task TestUpdateFieldsRaw(
            bool assetName, bool variableName,
            bool assetDesc, bool variableDesc,
            bool assetContext, bool variableContext,
            bool assetMeta, bool variableMeta)
        {
            var (handler, pusher) = tester.GetCDFPusher();
            using var extractor = tester.BuildExtractor(true, null, pusher);

            var upd = tester.Config.Extraction.Update;
            upd.Objects.Name = assetName;
            upd.Objects.Description = assetDesc;
            upd.Objects.Context = assetContext;
            upd.Objects.Metadata = assetMeta;
            upd.Variables.Name = variableName;
            upd.Variables.Description = variableDesc;
            upd.Variables.Context = variableContext;
            upd.Variables.Metadata = variableMeta;

            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Custom.Root, tester.Client);

            tester.Config.Cognite.RawMetadata = new RawMetadataConfig
            {
                Database = "metadata",
                AssetsTable = "assets",
                TimeseriesTable = "timeseries"
            };

            tester.Config.Extraction.DataTypes.AllowStringVariables = true;
            tester.Config.Extraction.DataTypes.MaxArraySize = 4;
            tester.Config.History.Enabled = false;

            var runTask = extractor.RunExtractor();

            await CommonTestUtils.WaitForCondition(() => handler.AssetRaw.Any() && handler.TimeseriesRaw.Any(), 5);

            CommonTestUtils.VerifyStartingConditions(handler.AssetRaw, handler.TimeseriesRaw
                .ToDictionary(kvp => kvp.Key, kvp => (TimeseriesDummy)kvp.Value), null, extractor, tester.Server.Ids.Custom, true);

            tester.Server.ModifyCustomServer();

            await extractor.Rebrowse();

            CommonTestUtils.VerifyStartingConditions(handler.AssetRaw, handler.TimeseriesRaw
                .ToDictionary(kvp => kvp.Key, kvp => (TimeseriesDummy)kvp.Value), upd, extractor, tester.Server.Ids.Custom, true);
            CommonTestUtils.VerifyModified(handler.AssetRaw, handler.TimeseriesRaw
                .ToDictionary(kvp => kvp.Key, kvp => (TimeseriesDummy)kvp.Value), upd, extractor, tester.Server.Ids.Custom, true);

            tester.Server.ResetCustomServer();
            tester.Config.Extraction.Update = new UpdateConfig();
            tester.Config.Cognite.RawMetadata = null;
            tester.Config.Extraction.DataTypes.AllowStringVariables = false;
            tester.Config.Extraction.DataTypes.MaxArraySize = 0;

            await BaseExtractorTestFixture.TerminateRunTask(runTask, extractor);
        }
        // Test for a specific bug
        [Fact]
        public async Task TestUpdateNullPropertyValue()
        {
            var (handler, pusher) = tester.GetCDFPusher();
            using var extractor = tester.BuildExtractor(true, null, pusher);

            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Wrong.Root, tester.Client);

            tester.Config.Extraction.DataTypes.MaxArraySize = 4;
            tester.Config.Extraction.Update = new UpdateConfig
            {
                Objects = new TypeUpdateConfig
                {
                    Metadata = true
                },
                Variables = new TypeUpdateConfig
                {
                    Metadata = true
                }
            };

            var runTask = extractor.RunExtractor();

            await CommonTestUtils.WaitForCondition(() => handler.Assets.Any() && handler.Timeseries.Any(), 5);

            var id = tester.Client.GetUniqueId(tester.Server.Ids.Wrong.RankImprecise);

            Assert.True(string.IsNullOrEmpty(handler.Assets[id].metadata["TooLargeDim"]));

            await extractor.Rebrowse();

            Assert.True(string.IsNullOrEmpty(handler.Assets[id].metadata["TooLargeDim"]));

            tester.Server.Server.MutateNode(tester.Server.Ids.Wrong.TooLargeProp, state =>
            {
                var varState = state as PropertyState;
                varState.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 5 });
                varState.Value = Enumerable.Range(0, 5).ToArray();
            });

            await extractor.Rebrowse();

            Assert.Equal("[0, 1, 2, 3, 4]", handler.Assets[id].metadata["TooLargeDim"]);

            await BaseExtractorTestFixture.TerminateRunTask(runTask, extractor);
        }
        #endregion

        #region transformations
        [Fact]
        public async Task TestPropertyInheritance()
        {
            // The IsProperty attribute is inherited by deeper nodes.
            using var pusher = new DummyPusher(new DummyPusherConfig());
            var extraction = tester.Config.Extraction;
            extraction.Transformations = new List<RawNodeTransformation>
            {
                new RawNodeTransformation
                {
                    Filter = new RawNodeFilter
                    {
                        Name = "^CustomRoot$"
                    },
                    Type = TransformationType.Property
                }
            };

            using var extractor = tester.BuildExtractor(true, null, pusher);

            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(ObjectIds.ObjectsFolder, tester.Client);

            extraction.DataTypes.AllowStringVariables = true;
            extraction.DataTypes.MaxArraySize = -1;
            extraction.DataTypes.AutoIdentifyTypes = true;

            await extractor.RunExtractor(true);

            var root = pusher.PushedNodes[ObjectIds.ObjectsFolder];
            var meta = root.BuildMetadata(null);
            Assert.Equal(17, meta.Count);
            // Verify that the metadata fields get values
            Assert.Equal("[0, 0, 0, 0]", meta["CustomRoot_Variable Array"]);
            Assert.Equal("String prop value", meta["CustomRoot_ChildObject2_StringProp"]);

            extraction.Transformations = null;
            extraction.DataTypes.AllowStringVariables = false;
            extraction.DataTypes.MaxArraySize = 0;
            extraction.DataTypes.AutoIdentifyTypes = false;
        }
        [Fact]
        public async Task TestLateIgnore()
        {
            using var pusher = new DummyPusher(new DummyPusherConfig());
            var extraction = tester.Config.Extraction;
            extraction.Transformations = new List<RawNodeTransformation>
            {
                new RawNodeTransformation
                {
                    Filter = new RawNodeFilter
                    {
                        Parent = new RawNodeFilter
                        {
                            Name = "^CustomRoot$"
                        }
                    },
                    Type = TransformationType.Ignore
                }
            };

            using var extractor = tester.BuildExtractor(true, null, pusher);

            tester.Config.Extraction.RootNode = CommonTestUtils.ToProtoNodeId(tester.Server.Ids.Custom.Root, tester.Client);

            extraction.DataTypes.AllowStringVariables = true;
            extraction.DataTypes.MaxArraySize = -1;
            extraction.DataTypes.AutoIdentifyTypes = true;

            await extractor.RunExtractor(true);

            extraction.Transformations = null;
            extraction.DataTypes.AllowStringVariables = false;
            extraction.DataTypes.MaxArraySize = 0;
            extraction.DataTypes.AutoIdentifyTypes = false;

            Assert.Single(pusher.PushedNodes);
            Assert.Empty(pusher.PushedVariables);
        }
        #endregion

        #region types
        [Fact]
        public async Task TestReadTypes()
        {
            using var pusher = new DummyPusher(new DummyPusherConfig());
            var extraction = tester.Config.Extraction;

            using var extractor = tester.BuildExtractor(true, null, pusher);

            extraction.RootNode = CommonTestUtils.ToProtoNodeId(ObjectIds.TypesFolder, tester.Client);
            extraction.NodeTypes.AsNodes = true;
            extraction.DataTypes.AllowStringVariables = true;
            extraction.DataTypes.MaxArraySize = -1;
            extraction.DataTypes.AutoIdentifyTypes = true;

            await extractor.RunExtractor(true);

            extraction.NodeTypes.AsNodes = false;
            extraction.DataTypes.AllowStringVariables = false;
            extraction.DataTypes.MaxArraySize = 0;
            extraction.DataTypes.AutoIdentifyTypes = false;

            Assert.Equal(449, pusher.PushedNodes.Count);
            Assert.Equal(366, pusher.PushedVariables.Count);

            var customVarType = pusher.PushedNodes[tester.Server.Ids.Custom.VariableType];
            Assert.Equal("CustomVariableType", customVarType.DisplayName);
            Assert.Equal(NodeClass.VariableType, customVarType.NodeClass);
            var meta = customVarType.BuildMetadata(extractor);
            Assert.Single(meta);
            Assert.Equal("123.123", meta["Value"]);

            var customObjType = pusher.PushedNodes[tester.Server.Ids.Custom.ObjectType];
            Assert.Equal("CustomObjectType", customObjType.DisplayName);
            Assert.Equal(NodeClass.ObjectType, customObjType.NodeClass);
        }
        #endregion
    }
}