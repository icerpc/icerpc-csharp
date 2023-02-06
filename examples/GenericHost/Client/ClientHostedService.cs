// Copyright (c) ZeroC, Inc.

using IceRpc;
using Microsoft.Extensions.Hosting;

namespace GenericHostExample;

/// <summary>The hosted client service is ran and managed by the .NET Generic Host.</summary>
public class ClientHostedService : BackgroundService
{
    // The host application lifetime is used to stop the .NET Generic Host.
    private readonly IHostApplicationLifetime _applicationLifetime;

    private readonly ClientConnection _connection;

    // A proxy to the remote Hello service.
    private readonly IHelloProxy _hello;

    // All the parameters are injected by the DI container.
    public ClientHostedService(
        IHelloProxy hello,
        ClientConnection connection,
        IHostApplicationLifetime applicationLifetime)
    {
        _applicationLifetime = applicationLifetime;
        _connection = connection;
        _hello = hello;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            string greeting = await _hello.SayHelloAsync(Environment.UserName, cancellationToken: stoppingToken);
            Console.WriteLine(greeting);
            await _connection.ShutdownAsync(stoppingToken);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Failed to connect to the server:\n{exception}");
        }

        // Stop the generic host once the invocation is done.
        _applicationLifetime.StopApplication();
    }
}
