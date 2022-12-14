// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace GenericHostExample;

public static class Program
{
    public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

    /// <summary>Creates the HostBuilder used to build the .NET Generic Host.</summary>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            // Set the content root path to the build directory of the server (e.g.: Client/bin/Debug/net7.0)
            .UseContentRoot(AppContext.BaseDirectory)

            // Configures the .NET Generic Host services.
            .ConfigureServices((hostContext, services) =>
            {
                // Add the ClientHostedService to the hosted services of the .NET Generic Host.
                services.AddHostedService<ClientHostedService>();

                // Bind the client connection options to the "appsettings.json" configuration "Client" section,
                // and add a Configure callback to configure its authentication options.
                services
                    .AddOptions<ClientConnectionOptions>()
                    .Bind(hostContext.Configuration.GetSection("Client"))
                    .Configure(options =>
                    {
                        // Configure the authentication options
                        var rootCA = new X509Certificate2(
                            hostContext.Configuration.GetValue<string>("CertificateAuthoritiesFile")!);

                        options.ClientAuthenticationOptions = new SslClientAuthenticationOptions
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

                services
                    .AddSingleton(_ => new ActivitySource("IceRpc"))
                    .AddIceRpcClientConnection()
                    .AddIceRpcInvoker(builder => builder.UseTelemetry().UseLogger().Into<ClientConnection>())
                    .AddIceRpcProxy<IHelloProxy, HelloProxy>();
            });

    /// <summary>The hosted client service is ran and managed by the .NET Generic Host.</summary>
    public class ClientHostedService : BackgroundService
    {
        // The host application lifetime is used to stop the .NET Generic Host.
        private readonly IHostApplicationLifetime _applicationLifetime;

        private readonly ClientConnection _connection;

        // A proxy to the remote Hello service.
        private readonly IHelloProxy _hello;

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
                Console.WriteLine($"failed to connect to the server:\n{exception}");
            }

            // Stop the generic host once the invocation is done.
            _applicationLifetime.StopApplication();
        }
    }
}
