﻿using Cognite.Extractor.Logging;
using Cognite.Extractor.Testing;
using Cognite.Extractor.Utils;
using Cognite.OpcUa;
using Microsoft.Extensions.DependencyInjection;
using Server;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Test.Utils;
using Xunit;
using Xunit.Abstractions;

namespace Test.Integration
{
    public class LauncherTestFixture : LoggingTestFixture, IAsyncLifetime
    {
        public int Port { get; }
        public ServerController Server { get; }
        public string EndpointUrl => $"opc.tcp://localhost:{Port}";

        public LauncherTestFixture()
        {
            Port = CommonTestUtils.NextPort;

            var services = new ServiceCollection();
            Configure(services);
            var provider = services.BuildServiceProvider();

            ThreadPool.SetMinThreads(20, 20);

            Server = new ServerController(new[] {
                PredefinedSetup.Custom, PredefinedSetup.Base, PredefinedSetup.Events,
                PredefinedSetup.Wrong, PredefinedSetup.Full, PredefinedSetup.Auditing }, provider, Port);
        }

        public async Task InitializeAsync()
        {
            await Server.Start();
        }

        public Task DisposeAsync()
        {
            Server?.Dispose();
            return Task.CompletedTask;
        }
    }

    public sealed class LauncherTests : IClassFixture<LauncherTestFixture>, IDisposable
    {
        private readonly LauncherTestFixture tester;
        private DummyPusher pusher;
        private UAExtractor extractor;
        public LauncherTests(ITestOutputHelper output, LauncherTestFixture tester)
        {
            this.tester = tester;
            tester.Init(output);
            Program.CommandDryRun = false;
            Program.OnLaunch = (s, o) => CommonBuild(s);
            ExtractorStarter.OnCreateExtractor = (d, e) =>
            {
                extractor = e;
            };
        }

        public void Dispose()
        {
            pusher?.Dispose();
            extractor?.Dispose();
        }

        private void CommonBuild(ServiceCollection services)
        {
            services.AddSingleton<IPusher, DummyPusher>(provider =>
            {
                pusher = new DummyPusher(new DummyPusherConfig());
                return pusher;
            });
        }

        private string GetConfigToolOutput()
        {
            return @"# This suggested configuration was generated by the ConfigurationTool.

source:
    endpoint-url: " + tester.EndpointUrl + Environment.NewLine
 + @"    attributes-chunk: 1000
    browse-throttling:
        max-node-parallelism: 1000
extraction:
    id-prefix: 
    namespace-map:
        http://opcfoundation.org/UA/: 'base:'
        opc.tcp://test.localhost: 'tl:'
    enable-audit-discovery: true
    data-types:
        custom-numeric-types:
          - node-id:
                namespace-uri: opc.tcp://test.localhost
                node-id: i=7
        max-array-size: 4
        allow-string-variables: true
        auto-identify-types: true
events:
    enabled: true
    history: true
history:
    enabled: true
    throttling:
        max-node-parallelism: 1000
version: 1
";
        }

        [Fact]
        public async Task TestRunToolNoConfig()
        {
            var args = new[]
            {
                "tool",
                "--endpoint-url",
                tester.EndpointUrl,
                "--no-config",
                "--auto-accept",
                "--config-target",
                "config-output-test-1.yml"
            };
            await Program.Main(args);

            var file = File.ReadAllText("config-output-test-1.yml");
            Assert.Equal(
                GetConfigToolOutput().Replace("\r\n", "\n", StringComparison.InvariantCulture),
                file.Replace("\r\n", "\n", StringComparison.InvariantCulture));
        }

        [Fact(Timeout = 20000)]
        public async Task TestRunExtractorNoConfig()
        {
            var args = new[]
            {
                "--endpoint-url",
                tester.EndpointUrl,
                "--no-config",
                "--auto-accept"
            };
            var task = Program.Main(args);

            try
            {
                await TestUtils.WaitForCondition(() => extractor != null, 10);

                await extractor.WaitForSubscriptions();

                await TestUtils.WaitForCondition(() => pusher.PushedNodes.Any(), 10);
                Assert.Equal(167, pusher.PushedNodes.Count);
                Assert.Equal(2006, pusher.PushedVariables.Count);
            }
            finally
            {
                await extractor?.Close();
                await Task.WhenAny(task, Task.Delay(5000));
            }
        }

        [Fact]
        public async Task TestRunExtractorToolConfig()
        {
            var args = new[]
            {
                "--auto-accept",
                "--config-file",
                "config-test-1.yml"
            };
            File.WriteAllText("config-test-1.yml", GetConfigToolOutput());

            var task = Program.Main(args);

            try
            {
                await TestUtils.WaitForCondition(() => extractor != null, 10);

                await extractor.WaitForSubscriptions();

                await TestUtils.WaitForCondition(() => pusher.PushedNodes.Any(), 10);
                Assert.Equal(172, pusher.PushedNodes.Count);
                Assert.Equal(2032, pusher.PushedVariables.Count);
            }
            finally
            {
                await extractor?.Close();
                await Task.WhenAny(task, Task.Delay(5000));
            }
        }

        [Fact]
        public async Task TestRunExtractorService()
        {
            var args = new[]
            {
                "--auto-accept",
                "--config-file",
                "config-test-1.yml",
                "--service",
                "--working-dir",
                Directory.GetCurrentDirectory(),
                "--config-root",
                "config"
            };
            File.WriteAllText("config-test-1.yml", GetConfigToolOutput());

            var task = Program.Main(args);

            try
            {
                await TestUtils.WaitForCondition(() => extractor != null, 10);

                await extractor.WaitForSubscriptions();

                await TestUtils.WaitForCondition(() => pusher.PushedNodes.Any(), 10);
                Assert.Equal(172, pusher.PushedNodes.Count);
                Assert.Equal(2032, pusher.PushedVariables.Count);
            }
            finally
            {
                if (extractor != null) await extractor.Close();
                await Task.WhenAny(task, Task.Delay(5000));
            }
        }

        [Fact]
        public async Task TestExtractorCLI()
        {
            BaseExtractorParams setup = null;
            Program.CommandDryRun = true;
            Program.OnLaunch = (s, o) => setup = o;
            var args = new[]
            {
                "--endpoint-url",
                "endpoint url",
                "--password",
                "password",
                "--user",
                "username",
                "--auto-accept",
                "--secure",
                "--config-file",
                "config file",
                "--config-dir",
                "config dir",
                "--log-dir",
                "log dir",
                "--no-config",
                "--log-level",
                "fatal",
                "--service",
                "--working-dir",
                "working dir",
                "--exit"
            };

            await Program.Main(args);

            var extSetup = setup as ExtractorParams;

            Assert.Equal("endpoint url", extSetup.EndpointUrl);
            Assert.Equal("password", extSetup.Password);
            Assert.Equal("username", extSetup.User);
            Assert.True(extSetup.AutoAccept);
            Assert.True(extSetup.Secure);
            Assert.Equal("config file", extSetup.ConfigFile);
            Assert.Equal("config dir", extSetup.ConfigDir);
            Assert.Equal("log dir", extSetup.LogDir);
            Assert.True(extSetup.NoConfig);
            Assert.Equal("fatal", extSetup.LogLevel);
            Assert.True(extSetup.Service);
            Assert.Equal("working dir", extSetup.WorkingDir);
            Assert.True(extSetup.Exit);

            args = new[]
            {
                "-e",
                "endpoint url",
                "-p",
                "password",
                "-u",
                "username",
                "--auto-accept",
                "--secure",
                "-f",
                "config file",
                "-d",
                "config dir",
                "--log-dir",
                "log dir",
                "-n",
                "-l",
                "fatal",
                "-s",
                "-w",
                "working dir",
                "-x"
            };

            await Program.Main(args);

            extSetup = setup as ExtractorParams;

            Assert.Equal("endpoint url", extSetup.EndpointUrl);
            Assert.Equal("password", extSetup.Password);
            Assert.Equal("username", extSetup.User);
            Assert.True(extSetup.AutoAccept);
            Assert.True(extSetup.Secure);
            Assert.Equal("config file", extSetup.ConfigFile);
            Assert.Equal("config dir", extSetup.ConfigDir);
            Assert.Equal("log dir", extSetup.LogDir);
            Assert.True(extSetup.NoConfig);
            Assert.Equal("fatal", extSetup.LogLevel);
            Assert.True(extSetup.Service);
            Assert.Equal("working dir", extSetup.WorkingDir);
            Assert.True(extSetup.Exit);

            args = new[]
            {
                "tool",
                "--config-target",
                "config target",
                "--endpoint-url",
                "endpoint url",
                "--password",
                "password",
                "--user",
                "username",
                "--auto-accept",
                "--secure",
                "--config-file",
                "config file",
                "--config-dir",
                "config dir",
                "--no-config",
                "--log-level",
                "fatal",
                "--working-dir",
                "working dir"
            };

            await Program.Main(args);

            var tSetup = setup as ConfigToolParams;

            Assert.True(tSetup.ConfigTool);
            Assert.Equal("config target", tSetup.ConfigTarget);
            Assert.Equal("endpoint url", tSetup.EndpointUrl);
            Assert.Equal("password", tSetup.Password);
            Assert.Equal("username", tSetup.User);
            Assert.True(tSetup.AutoAccept);
            Assert.True(tSetup.Secure);
            Assert.Equal("config file", tSetup.ConfigFile);
            Assert.Equal("config dir", tSetup.ConfigDir);
            Assert.True(tSetup.NoConfig);
            Assert.Equal("fatal", tSetup.LogLevel);
            Assert.Equal("working dir", tSetup.WorkingDir);

            args = new[]
            {
                "tool",
                "-o",
                "config target",
                "-e",
                "endpoint url",
                "-p",
                "password",
                "-u",
                "username",
                "--auto-accept",
                "--secure",
                "-f",
                "config file",
                "-d",
                "config dir",
                "-n",
                "-l",
                "fatal",
                "-w",
                "working dir"
            };

            await Program.Main(args);

            tSetup = setup as ConfigToolParams;

            Assert.True(tSetup.ConfigTool);
            Assert.Equal("config target", tSetup.ConfigTarget);
            Assert.Equal("endpoint url", tSetup.EndpointUrl);
            Assert.Equal("password", tSetup.Password);
            Assert.Equal("username", tSetup.User);
            Assert.True(tSetup.AutoAccept);
            Assert.True(tSetup.Secure);
            Assert.Equal("config file", tSetup.ConfigFile);
            Assert.Equal("config dir", tSetup.ConfigDir);
            Assert.True(tSetup.NoConfig);
            Assert.Equal("fatal", tSetup.LogLevel);
            Assert.Equal("working dir", tSetup.WorkingDir);
        }

        [Fact]
        public void TestVerifyAndBuildConfig()
        {
            var log = new DummyLogger();
            var method = typeof(ExtractorStarter).GetMethod("VerifyAndBuildConfig", BindingFlags.Static | BindingFlags.NonPublic);

            // Just plain run
            var config = new FullConfig();
            config.GenerateDefaults();
            config.Source.EndpointUrl = tester.EndpointUrl;
            var setup = new ExtractorParams();
            method.Invoke(typeof(ExtractorStarter), new object[] { log, config, setup, null, "config" });

            // Bind setup
            setup.EndpointUrl = "opc.tcp://localhost:60000";
            setup.User = "user";
            setup.Password = "password";
            setup.Secure = true;
            setup.LogLevel = "information";
            setup.LogDir = "logs";
            setup.AutoAccept = true;
            setup.Exit = true;

            method.Invoke(typeof(ExtractorStarter), new object[] { log, config, setup, null, "config" });
            Assert.Equal("config", config.Source.ConfigRoot);
            Assert.Equal("opc.tcp://localhost:60000", config.Source.EndpointUrl);
            Assert.Equal("user", config.Source.Username);
            Assert.Equal("password", config.Source.Password);
            Assert.True(config.Source.Secure);
            Assert.Equal("information", config.Logger.Console.Level);
            Assert.Equal("information", config.Logger.File.Level);
            Assert.Equal("logs", config.Logger.File.Path);
            Assert.True(config.Source.AutoAccept);
            Assert.True(config.Source.ExitOnFailure);

            // Alternate paths
            config = new FullConfig();
            config.GenerateDefaults();
            config.Logger.File = new FileConfig { Level = "debug" };
            config.Source.EndpointUrl = tester.EndpointUrl;
            setup = new ExtractorParams();
            setup.NoConfig = true;
            setup.LogDir = "logs2";
            setup.Exit = true;
            var options = new ExtractorRunnerParams<FullConfig, UAExtractor>();

            method.Invoke(typeof(ExtractorStarter), new object[] { log, config, setup, options, "config" });

            Assert.Equal("logs2", config.Logger.File.Path);
            Assert.Equal("debug", config.Logger.File.Level);
            Assert.True(config.Source.ExitOnFailure);
            Assert.True(options.Restart);
            Assert.Equal("information", config.Logger.Console.Level);

            // Invalid configs
            config = new FullConfig();
            config.GenerateDefaults();
            setup = new ExtractorParams();

            var exc = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(typeof(ExtractorStarter), new object[] { log, config, setup, options, "config" }));
            Assert.Equal("Invalid config: Missing endpoint-url", exc.InnerException.Message);

            config.Source.EndpointUrl = "invalidurl";
            exc = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(typeof(ExtractorStarter), new object[] { log, config, setup, options, "config" }));
            Assert.Equal("Invalid config: EndpointUrl is not a valid URI", exc.InnerException.Message);

            // Get warnings
            log.Events.Clear();
            config.Source.EndpointUrl = tester.EndpointUrl;
            method.Invoke(typeof(ExtractorStarter), new object[] { log, config, setup, options, "config" });
            Assert.Equal(2, log.Events.Where(evt => evt.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning).Count());

            // events idprefix
            config.Extraction.IdPrefix = "events.";
            exc = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(typeof(ExtractorStarter), new object[] { log, config, setup, options, "config" }));
            Assert.Equal("Invalid config: Do not use events. as id-prefix, as it is used internally", exc.InnerException.Message);

            // Invalid history start time
            config.Extraction.IdPrefix = null;
            config.History.StartTime = "2d-agoo";
            exc = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(typeof(ExtractorStarter), new object[] { log, config, setup, options, "config" }));
            Assert.Equal("Invalid config: Invalid history start time: 2d-agoo", exc.InnerException.Message);

            // Missing opc-ua xml config
            config.History.StartTime = null;
            exc = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(typeof(ExtractorStarter), new object[] { log, config, setup, options, "." }));
            Assert.Equal("Missing opc.ua.net.extractor.Config.xml in config folder .", exc.InnerException.Message);
        }
    }
}
