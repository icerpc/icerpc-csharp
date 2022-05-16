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
            // Set the content root path to the build directory of the server (e.g.: Client/bin/Debug/net6.0)
            .UseContentRoot(AppContext.BaseDirectory)

            // Configures the .NET Generic Host services.
            .ConfigureServices((hostContext, services) =>
            {
                // Add the ClientHostedService to the hosted services of the .NET Generic Host.
                services.AddHostedService<ClientHostedService>();

                // Bind the connection options to the "appsettings.json" configuration "Connection" section, and add a
                // Configure callback to configure its authentication options.
                services
                    .AddOptions<ClientHostedServiceOptions>()
                    .Bind(hostContext.Configuration.GetSection("Client"))
                    .Configure(options =>
                    {
                        // Configure the authentication options
                        var rootCA = new X509Certificate2(
                            hostContext.Configuration.GetValue<string>("CertificateAuthoritiesFile"));
                        options.AuthenticationOptions = new SslClientAuthenticationOptions()
                        {
                            // A certificate validation callback that uses the configured certificate authorities file
                            // to validate the peer certificates.
                            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                            {
                                using var customChain = new X509Chain();
                                customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                                customChain.ChainPolicy.DisableCertificateDownloads = true;
                                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                                customChain.ChainPolicy.CustomTrustStore.Add(rootCA);
                                return customChain.Build((X509Certificate2)certificate!);
                            }
                        };
                    });

                // Add an IInvoker singleton service.
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

    /// <summary>The options class for <see cref="ClientHostedService"/>.</summary>
    public class ClientHostedServiceOptions
    {
        public SslClientAuthenticationOptions? AuthenticationOptions { get; set; }
        public TimeSpan ConnectTimeout { get; set; }
        public Endpoint RemoteEndpoint { get; set; }
    }

    /// <summary>The hosted client service is ran and managed by the .NET Generic Host</summary>
    private class ClientHostedService : BackgroundService
    {
        // The host application lifetime is used to stop the .NET Generic Host.
        private readonly IHostApplicationLifetime _applicationLifetime;

        // The IceRPC connection to communicate with the IceRPC server.
        private readonly Connection _connection;

        // The IceRPC proxy to perform invocations on the Hello service from the IceRPC server.
        private readonly HelloPrx _proxy;

        public ClientHostedService(
            IInvoker invoker,
            IHostApplicationLifetime applicationLifetime,
            IOptions<ClientHostedServiceOptions> options)
        {
            _applicationLifetime = applicationLifetime;

            var connectionOptions = new ConnectionOptions
            {
                AuthenticationOptions = options.Value.AuthenticationOptions,
                ConnectTimeout = options.Value.ConnectTimeout,
                RemoteEndpoint = options.Value.RemoteEndpoint,
            };

            _connection = new Connection(connectionOptions);

            _proxy = HelloPrx.FromConnection(_connection, invoker: invoker);
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                Console.Write("To say hello to the server, type your name: ");
                if (Console.ReadLine() is string name)
                {
                    Console.WriteLine(await _proxy.SayHelloAsync(name, cancel: cancellationToken));
                }
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
