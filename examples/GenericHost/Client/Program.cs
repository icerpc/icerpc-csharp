// Copyright (c) ZeroC, Inc. All rights reserved.

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

namespace Demo;
public class Program
{
    public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

    /// <summary>Creates the HostBuilder used to build the .NET Generic Host.</summary>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            // Set the content root path to the build directory of the server (e.g.: Client/bin/Debug/net6.0)
            .UseContentRoot(AppContext.BaseDirectory)

            // Configures the .NET Generic Host services.
            .ConfigureServices((hostContext, services) =>
            {
                // Add the ClientHostedService to the hosted services of the .NET Generic Host.
                services.AddHostedService<ClientHostedService>();

                // Get the ConnectionOptions from the configuration and add it to the generic host options. The DI
                // container will inject it in services that require an IOptions<ConnectionOptions> dependency.
                services
                    .AddOptions<ConnectionOptions>()
                    .Bind(hostContext.Configuration.GetSection("Connection"))
                    .Configure(connectionOptions =>
                    {
                        // Configure the authentication options
                        connectionOptions.AuthenticationOptions = new SslClientAuthenticationOptions()
                        {
                            RemoteCertificateValidationCallback =
                                CertificateValidaton.GetServerCertificateValidationCallback(
                                    certificateAuthorities: new X509Certificate2Collection
                                    {
                                        new X509Certificate2(
                                            hostContext.Configuration.GetValue<string>("CertificateAuthoritiesFile"))
                                    })
                        };
                    });

                // Add an IInvoker singleton service. The DI container will inject it in services that require an
                // IInvoker dependency.
                services.AddSingleton<IInvoker>(serviceProvider =>
                    {
                        // The invoker is a pipeline configured with the logger and telemetry interceptors. The
                        // interceptors use the logger factory provided by the .NET Generic Host.
                        ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                        return new Pipeline()
                            .UseLogger(loggerFactory)
                            .UseTelemetry(new TelemetryOptions { LoggerFactory = loggerFactory });
                    });
            });

    /// <summary>The client hosted service is ran and managed by the .NET Generic Host</summary>
    private class ClientHostedService : BackgroundService
    {
        // The host application lifetime is used to stop the .NET Generic Host.
        private readonly IHostApplicationLifetime _applicationLifetime;
        // The IceRPC connection to communicate with the IceRPC server.
        private readonly Connection _connection;
        // The IceRPC proxy to perform invocations on the Hello service from the IceRPC server.
        private readonly HelloPrx _proxy;

        public ClientHostedService(
            IOptions<ConnectionOptions> connectionOptions,
            IInvoker invoker,
            IHostApplicationLifetime applicationLifetime)
        {
            _applicationLifetime = applicationLifetime;

            _connection = new Connection(connectionOptions);

            _proxy = HelloPrx.FromConnection(_connection, invoker: invoker);
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine(await _proxy.SayHelloAsync("Hello", cancel: cancellationToken));
            }
            catch (Exception exception)
            {
                Console.WriteLine($"failed to connect to the server:\n{exception}");
            }

            // Stop the generic host once the invocation is done.
            _applicationLifetime.StopApplication();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);

            // Shutdown the connection when the hosted service is stopped.
            await _connection.ShutdownAsync(cancellationToken);
        }
    }
}
