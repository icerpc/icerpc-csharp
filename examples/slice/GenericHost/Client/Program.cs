// Copyright (c) ZeroC, Inc.

using GenericHostClient;
using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using IceRpc.Slice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Configure the host.
IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    // Set the content root path to the build directory of the client (e.g.: Client/bin/Debug/net8.0)
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
                    Path.Combine(
                        hostContext.HostingEnvironment.ContentRootPath,
                        hostContext.Configuration.GetValue<string>("CertificateAuthoritiesFile")!));

                options.ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    // A certificate validation callback that uses the configured certificate authorities file to
                    // validate the peer certificates.
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                    {
                        if (certificate is X509Certificate2 peerCertificate)
                        {
                            using var customChain = new X509Chain();
                            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            customChain.ChainPolicy.DisableCertificateDownloads = true;
                            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                            customChain.ChainPolicy.CustomTrustStore.Add(rootCA);
                            return customChain.Build(peerCertificate);
                        }
                        else
                        {
                            return false;
                        }
                    }
                };
            });

        services
            // The activity source used by the telemetry interceptor.
            .AddSingleton(_ => new ActivitySource("IceRpc"))
            // Add a ClientConnection singleton. This ClientConnections uses the ClientConnectionOptions provided by the
            // the IOptions<ClientConnectionOptions> configured/bound above.
            .AddIceRpcClientConnection()
            // Add an invoker singleton; this invoker corresponds to the invocation pipeline. This invocation pipeline
            // flows into the ClientConnection singleton.
            .AddIceRpcInvoker(builder => builder.UseTelemetry().UseLogger().Into<ClientConnection>())
            // Add an IGreeter singleton that uses the invoker singleton registered above.
            .AddSingleton<IGreeter>(provider => provider.CreateSliceProxy<GreeterProxy>());
    });

// Build the host.
using IHost host = hostBuilder.Build();

// Run hosted program.
host.Run();
