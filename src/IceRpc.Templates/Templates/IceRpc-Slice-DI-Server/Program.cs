using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using IceRpc.Slice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using IceRpc_Slice_DI_Server;

// Configure the host.
IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    // Set the content root path to the build directory of the server (e.g.: Server/bin/Debug/net8.0)
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
    });

// Build the host.
using IHost host = hostBuilder.Build();

// Run hosted program.
host.Run();
