// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Builder;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Common;

public static class ServiceCollectionExtensions
{
    /// <summary>Adds Server and ClientConnection singletons, with the server listening on the specified server address
    /// and the client connection connecting to the server's server address.</summary>
    public static IServiceCollection AddClientServerTest(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure,
        Protocol protocol,
        string host = "") =>
        services.AddClientServerTest(protocol, host).AddIceRpcServer(configure);

    /// <summary>Adds Server and ClientConnection singletons, with the server listening on the specified server address
    /// and the client connection connecting to the server's server address.</summary>
    public static IServiceCollection AddClientServerTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        Protocol protocol,
        string host = "")
    {
        services.AddOptions<ServerOptions>().Configure(options => options.ConnectionOptions.Dispatcher = dispatcher);
        return services.AddClientServerTest(protocol, host).AddIceRpcServer();
    }

    public static IServiceCollection AddClientServerTest(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure) =>
        services.AddClientServerTest(configure, Protocol.IceRpc);

    public static IServiceCollection AddClientServerTest(this IServiceCollection services, IDispatcher dispatcher) =>
        services.AddClientServerTest(dispatcher, Protocol.IceRpc);

    /// <summary>Installs the coloc duplex transport.</summary>
    public static IServiceCollection AddColocTransport(this IServiceCollection services) => services
        .AddSingleton<ColocTransport>()
        .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ClientTransport)
        .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ServerTransport);

    /// <summary>Adds Listener and IDuplexConnection singletons, with the listener listening on the specified server
    /// address and the client connection connecting to the listener's server address.</summary>
    public static IServiceCollection AddDuplexTransportClientServerTest(
        this IServiceCollection services,
        Uri serverAddressUri) => services
            .AddSingleton(provider =>
                provider.GetRequiredService<IDuplexServerTransport>().Listen(
                    new ServerAddress(serverAddressUri),
                    provider.GetService<IOptions<DuplexConnectionOptions>>()?.Value ?? new(),
                    provider.GetService<SslServerAuthenticationOptions>()))
            .AddSingleton(provider =>
                provider.GetRequiredService<IDuplexClientTransport>().CreateConnection(
                    provider.GetRequiredService<IListener<IDuplexConnection>>().ServerAddress,
                    provider.GetService<IOptions<DuplexConnectionOptions>>()?.Value ?? new(),
                    provider.GetService<SslClientAuthenticationOptions>()));

    /// <summary>Adds Listener and IMultiplexedConnection singletons, with the listener listening on the specified
    /// server address and the client connection connecting to the listener's server address.</summary>
    public static IServiceCollection AddMultiplexedTransportClientServerTest(
        this IServiceCollection services,
        Uri serverAddressUri) => services
            .AddSingleton(provider =>
                provider.GetRequiredService<IMultiplexedServerTransport>().Listen(
                    new ServerAddress(serverAddressUri),
                    provider.GetService<IOptions<MultiplexedConnectionOptions>>()?.Value ?? new(),
                    provider.GetService<SslServerAuthenticationOptions>()))
            .AddSingleton(provider =>
                provider.GetRequiredService<IMultiplexedClientTransport>().CreateConnection(
                    provider.GetRequiredService<IListener<IMultiplexedConnection>>().ServerAddress,
                    provider.GetService<IOptions<MultiplexedConnectionOptions>>()?.Value ?? new(),
                    provider.GetService<SslClientAuthenticationOptions>()));

    /// <summary>Installs the Slic multiplex transport.</summary>
    public static IServiceCollection AddSlicTransport(this IServiceCollection services) => services
        .AddSingleton<IMultiplexedServerTransport>(
            provider => new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>()))
        .AddSingleton<IMultiplexedClientTransport>(
            provider => new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>()));

    public static IServiceCollection AddTcpTransport(this IServiceCollection services) => services
        .AddSingleton<IDuplexClientTransport>(provider =>
            new TcpClientTransport(provider.GetService<IOptions<TcpClientTransportOptions>>()?.Value ?? new()))
        .AddSingleton<IDuplexServerTransport>(provider => new TcpServerTransport(
            provider.GetService<IOptions<TcpServerTransportOptions>>()?.Value ?? new()));

    public static IServiceCollection AddTls(this IServiceCollection services) => services
        .AddSingleton(provider => new SslClientAuthenticationOptions
        {
            ClientCertificates = new X509CertificateCollection()
                    {
                        new X509Certificate2("../../../certs/client.p12", "password")
                    },
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                certificate?.Issuer.Contains("Ice Tests CA", StringComparison.Ordinal) ?? false
        })
        .AddSingleton(provider => new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = false,
            ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
        });

    private static IServiceCollection AddClientServerTest(
        this IServiceCollection services,
        Protocol protocol,
        string host)
    {
        var serverAddress = new ServerAddress(protocol) { Host = host.Length == 0 ? "colochost" : host };

        services
            .AddColocTransport()
            .AddSingleton<ILoggerFactory>(LogAttributeLoggerFactory.Instance)
            .AddSingleton(LogAttributeLoggerFactory.Instance.Logger)
            .AddIceRpcClientConnection();

        services.AddOptions<ServerOptions>().Configure(options => options.ServerAddress = serverAddress);

        // TODO: the following doesn't work quite well with other transports than coloc since the server address here
        // not the listen server address.
        services.AddOptions<ClientConnectionOptions>().Configure<Server>(
            (options, server) => options.ServerAddress = server.ServerAddress);

        return services;
    }
}
