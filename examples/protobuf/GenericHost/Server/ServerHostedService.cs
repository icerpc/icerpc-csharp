// Copyright (c) ZeroC, Inc.

using IceRpc;
using Microsoft.Extensions.Hosting;

namespace GenericHostServer;

/// <summary>The server hosted service is ran and managed by the .NET Generic Host.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "This class is instantiated dynamically by the dependency injection container.")]
internal class ServerHostedService : IHostedService
{
    // The IceRPC server accepts connections from IceRPC clients.
    private readonly Server _server;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Start listening for client connections.
        _server.Listen();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        // Shuts down the IceRPC server when the hosted service is stopped.
        _server.ShutdownAsync(cancellationToken);

    internal ServerHostedService(Server server) => _server = server;
}
