﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Tests
{
    public class KestrelConfigurationBuilderTests
    {
        private KestrelServerOptions CreateServerOptions()
        {
            var serverOptions = new KestrelServerOptions();
            serverOptions.ApplicationServices = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IDefaultHttpsProvider, DefaultHttpsProvider>()
                .BuildServiceProvider();
            return serverOptions;
        }

        [Fact]
        public void ConfigureNamedEndpoint_OnlyRunForMatchingConfig()
        {
            var found = false;
            var serverOptions = CreateServerOptions();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("Endpoints:Found:Url", "http://*:5001"),
            }).Build();
            serverOptions.Configure(config)
                .Endpoint("Found", endpointOptions => found = true)
                .Endpoint("NotFound", endpointOptions => throw new NotImplementedException())
                .Build();

            Assert.Single(serverOptions.ListenOptions);
            Assert.Equal(5001, serverOptions.ListenOptions[0].IPEndPoint.Port);

            Assert.True(found);
        }

        [Fact]
        public void ConfigureEndpoint_OnlyRunWhenBuildIsCalled()
        {
            var run = false;
            var serverOptions = CreateServerOptions();
            serverOptions.Configure()
                .LocalhostEndpoint(5001, endpointOptions => run = true);

            Assert.Empty(serverOptions.ListenOptions);

            serverOptions.ConfigurationBuilder.Build();

            Assert.Single(serverOptions.ListenOptions);
            Assert.Equal(5001, serverOptions.ListenOptions[0].IPEndPoint.Port);

            Assert.True(run);
        }

        [Fact]
        public void CallBuildTwice_OnlyRunsOnce()
        {
            var serverOptions = CreateServerOptions();
            var builder = serverOptions.Configure()
                .LocalhostEndpoint(5001);

            Assert.Empty(serverOptions.ListenOptions);
            Assert.Equal(builder, serverOptions.ConfigurationBuilder);

            builder.Build();

            Assert.Single(serverOptions.ListenOptions);
            Assert.Equal(5001, serverOptions.ListenOptions[0].IPEndPoint.Port);
            Assert.Null(serverOptions.ConfigurationBuilder);

            builder.Build();

            Assert.Single(serverOptions.ListenOptions);
            Assert.Equal(5001, serverOptions.ListenOptions[0].IPEndPoint.Port);
            Assert.Null(serverOptions.ConfigurationBuilder);
        }

        [Fact]
        public void Configure_IsReplacable()
        {
            var run1 = false;
            var serverOptions = CreateServerOptions();
            var config1 = new ConfigurationBuilder().AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("Endpoints:End1:Url", "http://*:5001"),
            }).Build();
            serverOptions.Configure(config1)
                .LocalhostEndpoint(5001, endpointOptions => run1 = true);

            Assert.Empty(serverOptions.ListenOptions);
            Assert.False(run1);

            var run2 = false;
            var config2 = new ConfigurationBuilder().AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("Endpoints:End2:Url", "http://*:5002"),
            }).Build();
            serverOptions.Configure(config2)
                .LocalhostEndpoint(5003, endpointOptions => run2 = true);

            serverOptions.ConfigurationBuilder.Build();

            Assert.Equal(2, serverOptions.ListenOptions.Count);
            Assert.Equal(5002, serverOptions.ListenOptions[0].IPEndPoint.Port);
            Assert.Equal(5003, serverOptions.ListenOptions[1].IPEndPoint.Port);

            Assert.False(run1);
            Assert.True(run2);
        }

        [Fact]
        public void ConfigureDefaultsAppliesToNewConfigureEndpoints()
        {
            var serverOptions = CreateServerOptions();

            serverOptions.ConfigureEndpointDefaults(opt =>
            {
                opt.NoDelay = false;
            });

            serverOptions.ConfigureHttpsDefaults(opt =>
            {
                opt.ServerCertificate = new X509Certificate2(TestResources.TestCertificatePath, "testPassword");
                opt.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            });

            var ran1 = false;
            var ran2 = false;
            var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("Endpoints:End1:Url", "https://*:5001"),
            }).Build();
            serverOptions.Configure(config)
                .Endpoint("End1", opt =>
                {
                    ran1 = true;
                    Assert.True(opt.IsHttps);
                    Assert.Equal(ClientCertificateMode.RequireCertificate, opt.Https.ClientCertificateMode);
                    Assert.False(opt.Listener.NoDelay);
                })
                .LocalhostEndpoint(5002, opt =>
                {
                    ran2 = true;
                    Assert.False(opt.NoDelay);
                })
                .Build();

            Assert.True(ran1);
            Assert.True(ran2);

            Assert.NotNull(serverOptions.ListenOptions[0].ConnectionAdapters.Where(adapter => adapter.IsHttps).SingleOrDefault());
            Assert.Null(serverOptions.ListenOptions[1].ConnectionAdapters.Where(adapter => adapter.IsHttps).SingleOrDefault());
        }

        [Fact]
        public void ConfigureEndpointDefaultCanEnableHttps()
        {
            var serverOptions = CreateServerOptions();

            serverOptions.ConfigureEndpointDefaults(opt =>
            {
                opt.NoDelay = false;
                opt.UseHttps(new X509Certificate2(TestResources.TestCertificatePath, "testPassword"));
            });

            serverOptions.ConfigureHttpsDefaults(opt =>
            {
                opt.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            });

            var ran1 = false;
            var ran2 = false;
            var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("Endpoints:End1:Url", "https://*:5001"),
            }).Build();
            serverOptions.Configure(config)
                .Endpoint("End1", opt =>
                {
                    ran1 = true;
                    Assert.True(opt.IsHttps);
                    Assert.Equal(ClientCertificateMode.RequireCertificate, opt.Https.ClientCertificateMode);
                    Assert.False(opt.Listener.NoDelay);
                })
                .LocalhostEndpoint(5002, opt =>
                {
                    ran2 = true;
                    Assert.False(opt.NoDelay);
                })
                .Build();

            Assert.True(ran1);
            Assert.True(ran2);

            // You only get Https once per endpoint.
            Assert.NotNull(serverOptions.ListenOptions[0].ConnectionAdapters.Where(adapter => adapter.IsHttps).SingleOrDefault());
            Assert.NotNull(serverOptions.ListenOptions[1].ConnectionAdapters.Where(adapter => adapter.IsHttps).SingleOrDefault());
        }
    }
}
