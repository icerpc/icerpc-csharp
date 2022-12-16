// Copyright (c) ZeroC, Inc. All rights reserved.

using GenericHostExample;
using IceRpc;
using IceRpc.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Build generic host.
IHost host = Host.CreateDefaultBuilder(args)
    // Set the content root path to the build directory of the server (e.g.: Server/bin/Debug/net7.0)
    .UseContentRoot(AppContext.BaseDirectory)

    // Configure the .NET Generic Host services.
    .ConfigureServices((hostContext, services) =>
    {
        // Add the ServerHostedService to the hosted services of the .NET Generic Host.
        services.AddHostedService<ServerHostedService>();

        services.AddSingleton(sp => new ActivitySource("IceRpc"));

        // Bind the server options to the "appsettings.json" configuration "Server" section, and add a Configure
        // callback to configure its authentication options.
        services
            .AddOptions<ServerOptions>()
            .Bind(hostContext.Configuration.GetSection("Server"))
            .Configure(options =>
            {
                options.ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate = new X509Certificate2(
                        hostContext.Configuration.GetValue<string>("Certificate:File")!,
                        hostContext.Configuration.GetValue<string>("Certificate:Password"))
                };
            });

        // Add the Slice service that implements Slice interface `Hello`, as a singleton.
        services.AddSingleton<IHello, Hello>();

        // Add a server and configure the dispatcher using a dispatcher builder
        services.AddIceRpcServer(
            builder => builder
                .UseTelemetry()
                .UseLogger()
                .Map<IHello>());
    })
    .Build();

// Run hosted program.
host.Run();

/// <summary>The server hosted service is ran and managed by the .NET Generic Host</summary>
public class ServerHostedService : IHostedService, IAsyncDisposable
{
    // The IceRPC server accepts connections from IceRPC clients.
    private readonly Server _server;

    public ServerHostedService(Server server) => _server = server;

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _server.DisposeAsync();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Start listening for client connections.
        _server.Listen();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        // Shuts down the IceRPC server when the hosted service is stopped.
        _server.ShutdownAsync(cancellationToken);
}
