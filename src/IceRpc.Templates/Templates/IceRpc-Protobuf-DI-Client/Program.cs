using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography.X509Certificates;

using IceRpc_Protobuf_DI_Client;

// Configure the host with the content root path set to the build directory (e.g.: Client/bin/Debug/net10.0).
HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

IServiceCollection services = hostBuilder.Services;

// Add the ClientHostedService to the hosted services of the .NET Generic Host.
services.AddHostedService<ClientHostedService>();

// Load and register the root CA certificate as a singleton so it stays alive and gets disposed.
services.AddSingleton<X509Certificate2>(_ =>
    X509CertificateLoader.LoadCertificateFromFile(
        Path.Combine(
            hostBuilder.Environment.ContentRootPath,
            hostBuilder.Configuration.GetValue<string>("CertificateAuthoritiesFile")!)));

// Bind the client connection options to the "appsettings.json" configuration "Client" section, and add a Configure
// callback to configure its authentication options.
services
    .AddOptions<ClientConnectionOptions>()
    .Bind(hostBuilder.Configuration.GetSection("Client"))
    .Configure<X509Certificate2>((options, rootCA) =>
        options.ClientAuthenticationOptions = CreateClientAuthenticationOptions(rootCA));

services
    // Add a ClientConnection singleton. This ClientConnection uses the ClientConnectionOptions provided by the
    // IOptions<ClientConnectionOptions> configured/bound above.
    .AddIceRpcClientConnection()
    // Add an invoker singleton; this invoker corresponds to the invocation pipeline. This invocation pipeline
    // flows into the ClientConnection singleton.
    .AddIceRpcInvoker(
        builder => builder
            .UseDeadline(hostBuilder.Configuration.GetValue<TimeSpan>("Deadline:DefaultTimeout"))
            .UseLogger()
            .Into<ClientConnection>())
    // Add an IGreeter singleton that uses the invoker singleton registered above.
    .AddSingleton<IGreeter>(provider => provider.CreateProtobufClient<GreeterClient>());

// Build and run the host.
using IHost host = hostBuilder.Build();
host.Run();
