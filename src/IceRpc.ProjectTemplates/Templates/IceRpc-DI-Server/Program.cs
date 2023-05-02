// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc;
using IceRpc.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Build generic host.
IHost host = Host.CreateDefaultBuilder(args)
    // Set the content root path to the build directory of the server (e.g.: Server/bin/Debug/net7.0)
    .UseContentRoot(AppContext.BaseDirectory)

    // Configure the .NET Generic Host services.
    .ConfigureServices((hostContext, services) =>
    {
        // Add the ServerHostedService to the hosted services of the .NET Generic Host.
        services.AddHostedService<ServerHostedService>();

        // Bind the server options to the "appsettings.json" configuration "Server" section.
        services
            .AddOptions<ServerOptions>()
            .Bind(hostContext.Configuration.GetSection("Server"));

        // Add the Slice service that implements Slice interface `Greeter`, as a singleton.
        services.AddSingleton<IGreeterService, Chatbot>();

        // Add a server and configure the dispatcher using a dispatcher builder. The server uses the ServerOptions
        // provided by the IOptions<ServerOptions> singleton configured/bound above.
        services.AddIceRpcServer(
            builder => builder
                .UseDeadline()
                .UseLogger()
                .Map<IGreeterService>());
    })
    .Build();

// Run hosted program.
host.Run();
