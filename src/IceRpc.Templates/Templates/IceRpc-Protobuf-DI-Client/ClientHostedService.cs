using IceRpc;
using Microsoft.Extensions.Hosting;

namespace IceRpc_Protobuf_DI_Client;

/// <summary>The hosted client service is ran and managed by the .NET Generic Host.</summary>
public class ClientHostedService : BackgroundService
{
    // The host application lifetime is used to stop the .NET Generic Host.
    private readonly IHostApplicationLifetime _applicationLifetime;

    private readonly ClientConnection _connection;

    // The IGreeter managed by the DI container.
    private readonly GreeterClient _greeter;

    // All the parameters are injected by the DI container.
    public ClientHostedService(
        IGreeter greeter,
        IInvoker invoker,
        IHostApplicationLifetime applicationLifetime)
    {
        _applicationLifetime = applicationLifetime;
        _connection = connection;
        _greeter = greeter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var request = new GreetRequest { Name = Environment.UserName };
            GreetResponse response = await _greeter.GreetAsync(request, cancellationToken: stoppingToken);
            Console.WriteLine(response.Greeting);
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
