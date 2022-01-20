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
    /// <summary>This class is used to read the server configuration options from the appsettings.json configuration
    /// file.</summary>
    public class ServerOptions
    {
        public ConnectionOptions ConnectionOptions { get; set; } = new();

        public Endpoint Endpoint { get; set; } = "icerpc://[::0]";
    }

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
                    services.AddOptions<ServerOptions>().Bind(hostContext.Configuration.GetSection("Server"));

                    // Create the client transport and add it as a singleton service of the generic host services. The
                    // DI container will inject it in services that require an
                    // IServerTransport<IMultiplexedNetworkConnection> dependency.
                    services.AddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
                        {
                            // Get the transport options from the configuration.
                            IConfiguration configuration = hostContext.Configuration.GetSection("Transport");
                            TcpServerOptions tcpOptions =
                                configuration?.GetValue<TcpServerOptions>("Tcp") ??
                                new()
                                {
                                    // Create the authentication options with the certificate defined in the configured
                                    // certificate file.
                                    AuthenticationOptions = new SslServerAuthenticationOptions()
                                    {
                                        ServerCertificate = new X509Certificate2(
                                            Path.Combine(
                                                hostContext.HostingEnvironment.ContentRootPath,
                                                configuration.GetValue<string>("CertificateFile")),
                                            configuration.GetValue<string>("CertificatePassword"))
                                    }
                                };
                            return new SlicServerTransport(new TcpServerTransport(tcpOptions));
                        });

                    // Add an IDispatcher singleton service. The DI container will inject it in services that require an
                    // IInvoker dependency.
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
                });

        /// <summary>The server hosted service is ran and managed by the .NET Generic Host</summary>
        private class ServerHostedService : IHostedService
        {
            // The IceRPC server to accept connections from IceRPC clients.
            private readonly Server _server;

            public ServerHostedService(
                IServerTransport<IMultiplexedNetworkConnection> serverTransport,
                IOptions<ServerOptions> options,
                IDispatcher dispatcher,
                ILoggerFactory loggerFactory) =>
                _server = new Server
                {
                    ConnectionOptions = options.Value.ConnectionOptions,
                    Dispatcher = dispatcher,
                    Endpoint = options.Value.Endpoint,
                    LoggerFactory = loggerFactory,
                    MultiplexedServerTransport = serverTransport
                };

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
