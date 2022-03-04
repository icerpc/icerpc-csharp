// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace ServerApp
{
    public class Program
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

                    // Get the ServerOptions from the configuration and add it to the generic host options. The DI
                    // container will inject it in services that require an IOptions<ServerOptions> dependency.
                    services
                        .AddOptions()
                        .AddOptions<ServerOptions>()
                        .Bind(hostContext.Configuration.GetSection("Server"))
                        .Configure(serverOptions =>
                            {
                                // Configure the authentication options
                                serverOptions.AuthenticationOptions = new SslServerAuthenticationOptions()
                                {
                                    ServerCertificate = new X509Certificate2(
                                        hostContext.Configuration.GetValue<string>(
                                            "Server:AuthenticationOptions:CertificateFile"),
                                        hostContext.Configuration.GetValue<string>(
                                            "Server:AuthenticationOptions:CertificatePassword"))
                                };
                            })
                        .Configure((ServerOptions serverOptions, ILoggerFactory loggerFactory) =>
                            {
                                // Configure the dispatcher
                                var router = new Router();
                                router.UseLogger(loggerFactory);
                                router.UseTelemetry(new TelemetryOptions { LoggerFactory = loggerFactory });
                                router.Map<IHello>(new Hello());
                                serverOptions.Dispatcher = router;
                            });
                });

        /// <summary>The server hosted service is ran and managed by the .NET Generic Host</summary>
        private class ServerHostedService : IHostedService
        {
            // The IceRPC server to accept connections from IceRPC clients.
            private readonly Server _server;

            public ServerHostedService(IOptions<ServerOptions> options) =>
                _server = new Server(options.Value);

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
}
