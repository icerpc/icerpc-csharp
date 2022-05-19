// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Configure;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Demo;

public static class Program
{
    public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

    /// <summary>Creates the HostBuilder used to build the .NET Generic Host.</summary>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            // Set the content root path to the build directory of the server (e.g.: Server/bin/Debug/net6.0)
            .UseContentRoot(AppContext.BaseDirectory)

            // Configure the .NET Generic Host services.
            .ConfigureServices((hostContext, services) =>
            {
                // Add the ServerHostedService to the hosted services of the .NET Generic Host.
                services.AddHostedService<ServerHostedService>();

                services.AddOptions<ServerOptions>().Configure(options =>
                {
                    Console.WriteLine("Configure server options:");
                    // TODO temporary until we refactor server options
                    options.Endpoint = hostContext.Configuration.GetValue<string>("Server:Endpoint");
                    options.CloseTimeout = hostContext.Configuration.GetValue<TimeSpan>("Server:CloseTimeout");
                    options.AuthenticationOptions = new SslServerAuthenticationOptions()
                    {
                        ServerCertificate = new X509Certificate2(
                            hostContext.Configuration.GetValue<string>("Certificate:File"),
                            hostContext.Configuration.GetValue<string>("Certificate:Password"))
                    };
                });

                services
                    .AddIceRpcServer()
                    .AddRouter(routerBuilder =>
                    {
                        Console.WriteLine("configure router");
                        routerBuilder.UseTelemetry();
                        routerBuilder.UseLogger();
                        routerBuilder.Map<IHello>(new Hello());
                    });
            });


    /// <summary>The server hosted service is ran and managed by the .NET Generic Host</summary>
    private class ServerHostedService : IHostedService, IAsyncDisposable
    {
        // The IceRPC server to accept connections from IceRPC clients.
        private readonly Server _server;

        public ServerHostedService(Server server) => _server = server;

        public ValueTask DisposeAsync() => _server.DisposeAsync();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Start listening for client connections.
            _server.Listen();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) =>
            // Shutdown the IceRPC server when the hosted service is stopped.
            _server.ShutdownAsync(cancellationToken);
    }
}
