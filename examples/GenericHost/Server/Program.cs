// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Builder;
using IceRpc.Logger;
using IceRpc.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Demo;

public static class Program
{
    public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

    /// <summary>Creates the HostBuilder used to build the .NET Generic Host.</summary>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
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
                                hostContext.Configuration.GetValue<string>("Certificate:File"),
                                hostContext.Configuration.GetValue<string>("Certificate:Password"))
                        };
                    });

                services.AddSingleton<IHello>(new Hello());

                // Add a server and configure the dispatcher using a dispatcher builder
                services.AddIceRpcServer(
                    builder => builder
                        .UseTelemetry()
                        .UseLogger()
                        .Map<IHello>());
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
