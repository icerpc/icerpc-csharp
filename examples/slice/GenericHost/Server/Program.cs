// Copyright (c) ZeroC, Inc.

using GenericHostServer;
using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Configure the host.
IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    // Set the content root path to the build directory of the server (e.g.: Server/bin/Debug/net8.0)
    .UseContentRoot(AppContext.BaseDirectory)

    // Configure the .NET Generic Host services.
    .ConfigureServices((hostContext, services) =>
    {
        // Add the ServerHostedService to the hosted services of the .NET Generic Host.
        services.AddHostedService<ServerHostedService>();

        // The activity source used by the telemetry interceptor.
        services.AddSingleton(sp => new ActivitySource("IceRpc"));

        string serverCert = Path.Combine(
            hostContext.HostingEnvironment.ContentRootPath,
            hostContext.Configuration.GetValue<string>("AuthenticationOptions:CertificateFile")!);

        string serverKey = Path.Combine(
            hostContext.HostingEnvironment.ContentRootPath,
            hostContext.Configuration.GetValue<string>("AuthenticationOptions:KeyFile")!);

        // The X509 certificate used by the server.
        services.AddSingleton(sp => X509Certificate2.CreateFromPemFile(serverCert, serverKey));

        // Bind the server options to the "appsettings.json" configuration "Server" section, and add a Configure
        // callback to configure its authentication options.
        services
            .AddOptions<ServerOptions>()
            .Bind(hostContext.Configuration.GetSection("Server"))
            .Configure((ServerOptions options, X509Certificate2 serverCertificate) =>
            {
                // Create a collection with the server certificate and any intermediate certificates. This is used by
                // ServerCertificateContext to provide the certificate chain to the peer.
                var intermediates = new X509Certificate2Collection();
                intermediates.ImportFromPemFile(serverCert);

                options.ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificateContext = SslStreamCertificateContext.Create(serverCertificate, intermediates)
                };
            });

        // Add the Slice service that implements Slice interface `Greeter`, as a singleton.
        services.AddSingleton<IGreeterService, Chatbot>();

        // Add a server and configure the dispatcher using a dispatcher builder. The server uses the ServerOptions
        // provided by the IOptions<ServerOptions> singleton configured/bound above.
        services.AddIceRpcServer(
            builder => builder
                .UseTelemetry()
                .UseLogger()
                .Map<IGreeterService>());
    });

// Build the host.
using IHost host = hostBuilder.Build();

// Run hosted program.
host.Run();
