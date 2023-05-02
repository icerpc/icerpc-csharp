// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc;
using IceRpc.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

// Build generic host.
IHost host = Host.CreateDefaultBuilder(args)
    // Set the content root path to the build directory of the client (e.g.: Client/bin/Debug/net7.0)
    .UseContentRoot(AppContext.BaseDirectory)

    // Configures the .NET Generic Host services.
    .ConfigureServices((hostContext, services) =>
    {
        // Add the ClientHostedService to the hosted services of the .NET Generic Host.
        services.AddHostedService<ClientHostedService>();

        // Bind the client connection options to the "appsettings.json" configuration "Client" section.
        services
            .AddOptions<ClientConnectionOptions>()
            .Bind(hostContext.Configuration.GetSection("Client"));

        services
            // Add a ClientConnection singleton. This ClientConnections uses the ClientConnectionOptions provided by the
            // the IOptions<ClientConnectionOptions> configured/bound above.
            .AddIceRpcClientConnection()
            // Add an invoker singleton; this invoker corresponds to the invocation pipeline. This invocation pipeline
            // flows into the ClientConnection singleton.
            .AddIceRpcInvoker(
                builder => builder
                    .UseDeadline(defaultTimeout: hostContext.Configuration.GetValue<TimeSpan>("Deadline:DefaultTimeout"))
                    .UseLogger()
                    .Into<ClientConnection>())
            // Add an IGreeter singleton using the invoker singleton registered above.
            .AddIceRpcProxy<IGreeter, GreeterProxy>();
    })
    .Build();

// Run hosted program.
host.Run();
