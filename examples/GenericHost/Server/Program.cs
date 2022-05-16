// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Configure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

                // Add an IDispatcher singleton service.
                services.AddSingleton<IDispatcher>(serviceProvider =>
                    {
                        // The dispatcher is a router configured with the logger and telemetry middlewares and the
                        // IHello service. The middlewares use the logger factory provided by the .NET Generic Host.
                        ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                        var router = new Router();
                        router.UseLogger(loggerFactory);
                        router.UseTelemetry(new TelemetryOptions { LoggerFactory = loggerFactory });
                        router.Map<IHello>(new Hello());
                        return router;
                    });

                // Bind the server hosted service options to the "appsettings.json" configuration "Server" section, and
                // add a Configure callback to configure its authentication options.
                services
                    .AddOptions<ServerHostedServiceOptions>()
                    .Bind(hostContext.Configuration.GetSection("Server"))
                    // Configure the authentication options
                    .Configure(options =>
                        options.AuthenticationOptions = new SslServerAuthenticationOptions()
                        {
                            ServerCertificate = new X509Certificate2(
                                hostContext.Configuration.GetValue<string>("Certificate:File"),
                                hostContext.Configuration.GetValue<string>("Certificate:Password"))
                        });
            });

    /// <summary>The options class for <see cref="ClientHostedService"/>.</summary>
    public class ServerHostedServiceOptions
    {
        public SslServerAuthenticationOptions? AuthenticationOptions { get; set; }

        public TimeSpan CloseTimeout { get; set; }

        public Endpoint Endpoint { get; set; }
    }

    /// <summary>The server hosted service is ran and managed by the .NET Generic Host</summary>
    private class ServerHostedService : IHostedService, IAsyncDisposable
    {
        // The IceRPC server to accept connections from IceRPC clients.
        private readonly Server _server;

        public ServerHostedService(
            IDispatcher dispatcher,
            IOptions<ServerHostedServiceOptions> options)
        {
            var serverOptions = new ServerOptions
            {
                AuthenticationOptions = options.Value.AuthenticationOptions,
                CloseTimeout = options.Value.CloseTimeout,
                Dispatcher = dispatcher,
                Endpoint = options.Value.Endpoint,
            };

            _server = new Server(serverOptions);
        }

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
