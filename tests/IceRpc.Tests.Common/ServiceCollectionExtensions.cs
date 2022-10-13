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
    /// <summary>Adds Server and ClientConnection singletons, with the server listening on the specified host.</summary>
    public static IServiceCollection AddClientServerColocTest(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure,
        Protocol protocol,
        string host = "") =>
        services.AddClientServerColocTest(protocol, host).AddIceRpcServer(configure);

    /// <summary>Adds Server and ClientConnection singletons, with the server listening on the specified host.</summary>
    public static IServiceCollection AddClientServerColocTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        Protocol protocol,
        string host = "")
    {
        services.AddOptions<ServerOptions>().Configure(options => options.ConnectionOptions.Dispatcher = dispatcher);
        return services.AddClientServerColocTest(protocol, host).AddIceRpcServer();
    }

    public static IServiceCollection AddClientServerColocTest(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure) =>
        services.AddClientServerColocTest(configure, Protocol.IceRpc);

    public static IServiceCollection AddClientServerColocTest(
        this IServiceCollection services,
        IDispatcher dispatcher) =>
        services.AddClientServerColocTest(dispatcher, Protocol.IceRpc);

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

    /// <summary>Installs the Slic multiplexed transport.</summary>
    public static IServiceCollection AddSlicTransport(this IServiceCollection services) => services
        .AddSingleton<IMultiplexedServerTransport>(
            provider => new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>()))
        .AddSingleton<IMultiplexedClientTransport>(
            provider => new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>()));

    public static IServiceCollection AddSslAuthenticationOptions(this IServiceCollection services) => services
        .AddSingleton(provider => new SslClientAuthenticationOptions
        {
            ClientCertificates = new X509CertificateCollection
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

    private static IServiceCollection AddClientServerColocTest(
        this IServiceCollection services,
        Protocol protocol,
        string host)
    {
        var serverAddress = new ServerAddress(protocol) { Host = host.Length == 0 ? "colochost" : host };

        // Note: the multiplexed transport is added by IceRpcServer/IceRpcClientConnection.
        services
            .AddColocTransport()
            .AddSingleton<ILoggerFactory>(LogAttributeLoggerFactory.Instance)
            .AddSingleton(LogAttributeLoggerFactory.Instance.Logger)
            .AddIceRpcClientConnection();

        services.AddOptions<ServerOptions>().Configure(options => options.ServerAddress = serverAddress);
        services.AddOptions<ClientConnectionOptions>().Configure(options => options.ServerAddress = serverAddress);

        return services;
    }
}
