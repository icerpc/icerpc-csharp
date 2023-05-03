// Copyright (c) ZeroC, Inc.

using GenericHostExample;
using IceRpc;
using IceRpc.Builder;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Configure the host.
var hostBuilder = Host.CreateDefaultBuilder(args)
    // Set the content root path to the build directory of the client (e.g.: Client/bin/Debug/net7.0)
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
                    // A certificate validation callback that uses the configured certificate authorities file to
                    // validate the peer certificates.
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
            // The activity source used by the telemetry interceptor.
            .AddSingleton(_ => new ActivitySource("IceRpc"))
            // Add a ClientConnection singleton. This ClientConnections uses the ClientConnectionOptions provided by the
            // the IOptions<ClientConnectionOptions> configured/bound above.
            .AddIceRpcClientConnection()
            // Add an invoker singleton; this invoker corresponds to the invocation pipeline. This invocation pipeline
            // flows into the ClientConnection singleton.
            .AddIceRpcInvoker(builder => builder.UseTelemetry().UseLogger().Into<ClientConnection>())
            // Add an IGreeter singleton using the invoker singleton registered above.
            .AddIceRpcProxy<IGreeter, GreeterProxy>();
    });

// Build the host.
using IHost host = hostBuilder.Build();

// Run hosted program.
host.Run();
