// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using IceRpc.Transports.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Security;

namespace IceRpc.Tests;

public static class ServiceCollectionExtensions
{
    /// <summary>Adds a Server and ClientConnection singletons, with the server listening on the specified endpoint and
    /// the client connection connecting to the server's endpoint.</summary>
    /// <remarks>When the endpoint's port is 0 and transport is not coloc, you need to create the server and call Listen
    ///  on it before creating the client connection.</remarks>
    public static IServiceCollection AddClientServerTest(this IServiceCollection services, Endpoint endpoint)
    {
        services
            .AddOptions<ServerOptions>()
            .Configure<IDispatcher>(
                (options, dispatcher) =>
                {
                    // Console.WriteLine("configuring dispatcher and endpoint");
                    options.ConnectionOptions.Dispatcher = dispatcher;
                    options.Endpoint = endpoint;
                });

        services.AddSingleton(provider =>
            new Server(
                provider.GetRequiredService<IOptions<ServerOptions>>().Value,
                loggerFactory: provider.GetService<ILoggerFactory>(),
                multiplexedServerTransport: provider.GetService<IServerTransport<IMultiplexedNetworkConnection>>(),
                simpleServerTransport: provider.GetService<IServerTransport<ISimpleNetworkConnection>>()));

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure<Server>((options, server) => options.RemoteEndpoint = server.Endpoint);

        services.AddSingleton(provider =>
            new ClientConnection(
                provider.GetRequiredService<IOptions<ClientConnectionOptions>>().Value,
                loggerFactory: provider.GetService<ILoggerFactory>(),
                multiplexedClientTransport: provider.GetService<IClientTransport<IMultiplexedNetworkConnection>>(),
                simpleClientTransport: provider.GetService<IClientTransport<ISimpleNetworkConnection>>()));

        return services;
    }

    /// <summary>Installs coloc as the default client and server transports for both ice and icerpc.</summary>
    public static IServiceCollection AddColoc(this IServiceCollection services, Protocol protocol)
    {
        services.AddSingleton<ColocTransport>();
        services.AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ClientTransport);
        services.AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ServerTransport);

        if (protocol == Protocol.IceRpc)
        {
            services.AddSlic();
        }
        return services;
    }

    /// <summary>Installs coloc client-server test.</summary>
    public static IServiceCollection AddColocTest(this IServiceCollection services, Protocol protocol) =>
        services
            .AddColoc(protocol)
            .AddClientServerTest(new Endpoint(protocol) { Host = "colochost" });

    public static IServiceCollection AddColocTest(this IServiceCollection services) =>
        services.AddColocTest(Protocol.IceRpc);

    /// <summary>Installs the Slic multiplexed transports over the registered simple transports.</summary>
    public static IServiceCollection AddSlic(this IServiceCollection services)
    {
        services
            .AddOptions<SlicServerTransportOptions>()
            .Configure<IServerTransport<ISimpleNetworkConnection>, IOptions<MultiplexedTransportOptions>>(
                (options, simpleServerTransport, multiplexedOptions) =>
                {
                    options.SimpleServerTransport = simpleServerTransport;

                    // TODO: do we really need this extra MultiplexedTransportOptions?
                    // and if we do, why does it have nullable properties?

                    options.BidirectionalStreamMaxCount =
                        multiplexedOptions.Value.BidirectionalStreamMaxCount ?? options.BidirectionalStreamMaxCount;

                    options.UnidirectionalStreamMaxCount =
                        multiplexedOptions.Value.UnidirectionalStreamMaxCount ?? options.UnidirectionalStreamMaxCount;
                });

        services.AddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(provider =>
            new SlicServerTransport(provider.GetRequiredService<IOptions<SlicServerTransportOptions>>().Value));

        services
            .AddOptions<SlicClientTransportOptions>()
            .Configure<IClientTransport<ISimpleNetworkConnection>, IOptions<MultiplexedTransportOptions>>(
                (options, simpleClientTransport, multiplexedOptions) =>
                {
                    options.SimpleClientTransport = simpleClientTransport;

                    // TODO: do we really need this extra MultiplexedTransportOptions?
                    // and if we do, why does it have nullable properties?

                    options.BidirectionalStreamMaxCount =
                        multiplexedOptions.Value.BidirectionalStreamMaxCount ?? options.BidirectionalStreamMaxCount;

                    options.UnidirectionalStreamMaxCount =
                        multiplexedOptions.Value.UnidirectionalStreamMaxCount ?? options.UnidirectionalStreamMaxCount;
                });

        services.AddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(provider =>
            new SlicClientTransport(provider.GetRequiredService<IOptions<SlicClientTransportOptions>>().Value));

        return services;
    }

    public static IServiceCollection AddTcp(
        this IServiceCollection services,
        Protocol protocol,
        TcpServerTransportOptions tcpServerTransportOptions,
        TcpClientTransportOptions tcpClientTransportOptions)
    {
        services.AddSingleton<IServerTransport<ISimpleNetworkConnection>>(
            _ => new TcpServerTransport(tcpServerTransportOptions));

        services.AddSingleton<IClientTransport<ISimpleNetworkConnection>>(
            _ => new TcpClientTransport(tcpClientTransportOptions));

        if (protocol == Protocol.IceRpc)
        {
            services.AddSlic();
        }
        return services;
    }

    public static IServiceCollection AddTcpTest(
        this IServiceCollection services,
        Protocol protocol,
        TcpServerTransportOptions tcpServerTransportOptions,
        TcpClientTransportOptions tcpClientTransportOptions) =>
        services
            .AddTcp(protocol, tcpServerTransportOptions, tcpClientTransportOptions)
            .AddClientServerTest(new Endpoint(protocol) { Host = "127.0.0.1", Port = 0 });

    public static IServiceCollection UseColoc(this IServiceCollection collection) =>
        collection.UseColoc(new ColocTransport());

    public static IServiceCollection UseColoc(this IServiceCollection collection, ColocTransport coloc)
    {
        collection.AddScoped(_ => coloc.ServerTransport);
        collection.AddScoped(_ => coloc.ClientTransport);
        collection.AddScoped(
            typeof(Endpoint),
            provider =>
            {
                string protocol = provider.GetService<Protocol>()?.Name ?? "icerpc";
                return Endpoint.FromString($"{protocol}://{Guid.NewGuid()}/");
            });
        return collection;
    }

    public static IServiceCollection UseDispatcher(this IServiceCollection collection, IDispatcher dispatcher) =>
        collection.AddScoped(_ => dispatcher);

    public static IServiceCollection UseProtocol(this IServiceCollection collection, string protocol) =>
        collection.AddScoped(_ => Protocol.FromString(protocol));

    public static IServiceCollection UseSlic(this IServiceCollection collection)
    {
        collection.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var simpleServerTransport = provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
            var serverOptions = provider.GetService<SlicServerTransportOptions>() ?? new SlicServerTransportOptions();
            var multiplexedTransportOptions = provider.GetService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions?.BidirectionalStreamMaxCount is int bidirectionalStreamMaxCount)
            {
                serverOptions.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount;
            }
            if (multiplexedTransportOptions?.UnidirectionalStreamMaxCount is int unidirectionalStreamMaxCount)
            {
                serverOptions.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount;
            }
            serverOptions.SimpleServerTransport = simpleServerTransport;
            return new SlicServerTransport(serverOptions);
        });

        collection.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var simpleClientTransport = provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();
            var clientOptions = provider.GetService<SlicClientTransportOptions>() ?? new SlicClientTransportOptions();
            var multiplexedTransportOptions = provider.GetService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions?.BidirectionalStreamMaxCount is int bidirectionalStreamMaxCount)
            {
                clientOptions.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount;
            }
            if (multiplexedTransportOptions?.UnidirectionalStreamMaxCount is int unidirectionalStreamMaxCount)
            {
                clientOptions.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount;
            }
            clientOptions.SimpleClientTransport = simpleClientTransport;
            return new SlicClientTransport(clientOptions);
        });

        collection.AddScoped<IListener<IMultiplexedNetworkConnection>>(provider =>
        {
            var serverTransport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
            return serverTransport.Listen(
                (Endpoint)provider.GetRequiredService(typeof(Endpoint)),
                null,
                NullLogger.Instance);
        });
        return collection;
    }

    public static ServiceCollection UseSimpleTransport(this ServiceCollection collection)
    {
        collection.AddScoped(provider =>
        {
            SslServerAuthenticationOptions? serverAuthenticationOptions =
                provider.GetService<SslServerAuthenticationOptions>();
            IServerTransport<ISimpleNetworkConnection>? serverTransport =
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
            return serverTransport.Listen(
                provider.GetRequiredService<Endpoint>(),
                serverAuthenticationOptions,
                NullLogger.Instance);
        });

        collection.AddScoped(provider =>
        {
            SslClientAuthenticationOptions? clientAuthenticationOptions =
                provider.GetService<SslClientAuthenticationOptions>();
            IListener<ISimpleNetworkConnection> listener =
                provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();
            IClientTransport<ISimpleNetworkConnection> clientTransport =
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();
            return clientTransport.CreateConnection(
                listener.Endpoint,
                clientAuthenticationOptions,
                NullLogger.Instance);
        });
        return collection;
    }

    public static ServiceCollection UseTcp(
        this ServiceCollection collection,
        TcpServerTransportOptions? serverTransportOptions = null,
        TcpClientTransportOptions? clientTransportOptions = null)
    {
        collection.AddScoped<IServerTransport<ISimpleNetworkConnection>>(
            provider => new TcpServerTransport(
                serverTransportOptions ??
                provider.GetService<TcpServerTransportOptions>() ??
                new TcpServerTransportOptions()));

        collection.AddScoped<IClientTransport<ISimpleNetworkConnection>>(
            provider => new TcpClientTransport(
                clientTransportOptions ??
                provider.GetService<TcpClientTransportOptions>() ??
                new TcpClientTransportOptions()));

        collection.AddScoped(
            typeof(Endpoint),
            provider =>
            {
                string protocol = provider.GetService<Protocol>()?.Name ?? "icerpc";
                return Endpoint.FromString($"{protocol}://127.0.0.1:0/");
            });

        return collection;
    }

    public static IServiceCollection UseConnectionOptions(
        this IServiceCollection collection,
        ClientConnectionOptions options) =>
        collection.AddSingleton(options);

    public static IServiceCollection UseServerOptions(this IServiceCollection collection, ServerOptions options) =>
        collection.AddSingleton(options);
}
