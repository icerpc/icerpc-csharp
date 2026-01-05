// Copyright (c) ZeroC, Inc.

using GenericHostClient;
using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using IceRpc.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Configure the host.
IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    // Set the content root path to the build directory of the client (e.g.: Client/bin/Debug/net10.0)
    .UseContentRoot(AppContext.BaseDirectory)

    // Configures the .NET Generic Host services.
    .ConfigureServices((hostContext, services) =>
    {
        // Add the ClientHostedService to the hosted services of the .NET Generic Host.
        services.AddHostedService<ClientHostedService>();

        // Load and register the root CA certificate as a singleton so it stays alive and gets disposed.
        services.AddSingleton<X509Certificate2>(sp =>
            X509CertificateLoader.LoadCertificateFromFile(
                Path.Combine(
                    hostContext.HostingEnvironment.ContentRootPath,
                    hostContext.Configuration.GetValue<string>("CertificateAuthoritiesFile")!)));

        // Bind the client connection options to the "appsettings.json" configuration "Client" section,
        // and add a Configure callback to configure its authentication options.
        services
            .AddOptions<ClientConnectionOptions>()
            .Bind(hostContext.Configuration.GetSection("Client"))
            .Configure<X509Certificate2>((options, rootCA) =>
                options.ClientAuthenticationOptions = CreateClientAuthenticationOptions(rootCA));

        services
            // The activity source used by the telemetry interceptor.
            .AddSingleton(_ => new ActivitySource("IceRpc"))
            // Add a ClientConnection singleton. This ClientConnections uses the ClientConnectionOptions provided by the
            // the IOptions<ClientConnectionOptions> configured/bound above.
            .AddIceRpcClientConnection()
            // Add an invoker singleton; this invoker corresponds to the invocation pipeline. This invocation pipeline
            // flows into the ClientConnection singleton.
            .AddIceRpcInvoker(builder => builder.UseTelemetry().UseLogger().Into<ClientConnection>())
            // Add an IGreeter singleton that uses the invoker singleton registered above.
            .AddSingleton<IGreeter>(provider => provider.CreateProtobufClient<GreeterClient>());
    });

// Build the host.
using IHost host = hostBuilder.Build();

// Run hosted program.
host.Run();
