// Copyright (c) ZeroC, Inc.

using IceRpc;
using Microsoft.Extensions.Hosting;

namespace GenericHostServer;

/// <summary>The server hosted service is ran and managed by the .NET Generic Host</summary>
public class ServerHostedService : IHostedService
{
    // The IceRPC server accepts connections from IceRPC clients.
    private readonly Server _server;

    public ServerHostedService(Server server) => _server = server;

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
