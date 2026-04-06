using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using IceRpc.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography.X509Certificates;

using IceRpc_Protobuf_DI_Server;

// Configure the host with the content root path set to the build directory (e.g.: Server/bin/Debug/net10.0).
HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

IServiceCollection services = hostBuilder.Services;

// Add the ServerHostedService to the hosted services of the .NET Generic Host.
services.AddHostedService<ServerHostedService>();

// Load and register the server certificate as a singleton so it stays alive and gets disposed.
services.AddSingleton<X509Certificate2>(_ =>
    X509CertificateLoader.LoadPkcs12FromFile(
        Path.Combine(
            hostBuilder.Environment.ContentRootPath,
            hostBuilder.Configuration.GetValue<string>("Certificate:File")!),
        password: null,
        keyStorageFlags: X509KeyStorageFlags.Exportable));

// Bind the server options to the "appsettings.json" configuration "Server" section, and add a Configure
// callback to configure its authentication options.
services
    .AddOptions<ServerOptions>()
    .Bind(hostBuilder.Configuration.GetSection("Server"))
    .Configure<X509Certificate2>((options, serverCertificate) =>
        options.ServerAuthenticationOptions = CreateServerAuthenticationOptions(serverCertificate));

// Add the Protobuf service that implements Protobuf service `Greeter`, as a singleton.
services.AddSingleton<IGreeterService, Chatbot>();

// Add a server and configure the dispatcher using a dispatcher builder. The server uses the ServerOptions
// provided by the IOptions<ServerOptions> singleton configured/bound above.
services.AddIceRpcServer(
    builder => builder
        .UseDeadline()
        .UseLogger()
        .Map<IGreeterService>());

// Build and run the host.
using IHost host = hostBuilder.Build();
host.Run();
