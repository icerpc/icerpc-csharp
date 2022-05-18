// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>A builder for configuring IceRpc servers.</summary>
public class ServerBuilder
{
    /// <summary>The builder service collection.</summary>
    public IServiceCollection ServiceCollection { get; }

    /// <summary>The builder service provider.</summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>Construct a server builder.</summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="serviceProvider">The service provider.</param>
    public ServerBuilder(IServiceCollection serviceCollection, IServiceProvider serviceProvider)
    {
        ServiceCollection = serviceCollection;
        ServiceProvider = serviceProvider;
    }

    /// <summary>Configure the server dispatcher.</summary>
    /// <param name="dispatcher">The dispatcher.</param>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseDispatcher(IDispatcher dispatcher)
    {
        ServiceCollection.AddOptions<ServerOptions>().Configure(options => options.Dispatcher = dispatcher);
        return this;
    }

    /// <summary>Configure the server close timeout.</summary>
    /// <param name="closeTimeout">This timeout is used when gracefully closing a connection to wait for the peer
    /// connection closure.</param>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseCloseTimeout(TimeSpan closeTimeout)
    {
        ServiceCollection.AddOptions<ServerOptions>().Configure(
            options =>
            {
                Console.WriteLine($"3 configure close timeout: {closeTimeout}");
                options.CloseTimeout = closeTimeout;
            });
        return this;
    }

    /// <summary>Configure the server connect timeout.</summary>
    /// <param name="connectTimeout">The connection establishment timeout value.</param>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseConnectTimeout(TimeSpan connectTimeout)
    {
        ServiceCollection.AddOptions<ServerOptions>().Configure(options => options.ConnectTimeout = connectTimeout);
        return this;
    }

    /// <summary>Configure the server endpoint.</summary>
    /// <param name="endpoint">The server's endpoint.</param>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseEndpoint(Endpoint endpoint)
    {
        Console.WriteLine($"1 use endpoint: {endpoint}");
        if (!endpoint.Protocol.IsSupported)
        {
            throw new NotSupportedException($"cannot set endpoint with protocol '{endpoint.Protocol}'");
        }
        Console.WriteLine($"2 configure endpoint: {endpoint}");
        ServiceCollection.AddOptions<ServerOptions>().Configure(options =>
            {
                Console.WriteLine($"3 configure endpoint: {endpoint}");
                options.Endpoint = endpoint;
            });
        return this;
    }

    /// <summary>Configure the server router.</summary>
    public ServerBuilder UseRouter(Action<RouterBuilder> configure)
    {
        var router = new RouterBuilder(ServiceCollection, ServiceProvider);
        configure(router);
        UseDispatcher(router.Build());
        return this;
    }

    /// <summary>Adds the slic multiplexed transport to the server builder.</summary>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseSlic()
    {
        ServiceCollection.AddOptions<SlicServerTransportOptions>();
        ServiceCollection.AddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
            provider =>
            {
                IOptions<SlicServerTransportOptions> options =
                    provider.GetRequiredService<IOptions<SlicServerTransportOptions>>();
                return new SlicServerTransport(options.Value);
            });
        return this;
    }

    /// <summary>Configures the ssl server authentication options.</summary>
    /// <param name="configure">The action to configure the provided <see cref="SslServerAuthenticationOptions"/>.
    /// </param>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseSsl(Action<SslServerAuthenticationOptions> configure)
    {
        ServiceCollection.AddOptions<SslServerAuthenticationOptions>().Configure(configure);
        return this;
    }

    /// <summary>Configures the X509 certificate used by the server.</summary>
    /// <param name="certificate">The X509 certificate.</param>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseServerCertificate(X509Certificate certificate)
    {
        ServiceCollection.AddOptions<SslServerAuthenticationOptions>().Configure(
            options => options.ServerCertificate = certificate);
        return this;
    }

    /// <summary>Adds the tcp simple transport to the server builder.</summary>
    /// <param name="configure">Action to configure the provided <see cref="TcpServerTransportOptions"/>.</param>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseSlic(Action<SlicServerTransportOptions> configure)
    {
        ServiceCollection.AddOptions<SlicServerTransportOptions>().Configure(configure);
        return this;
    }

    /// <summary>Adds the tcp simple transport to the server builder.</summary>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseTcp()
    {
        ServiceCollection.AddOptions<TcpServerTransportOptions>();
        ServiceCollection.AddSingleton<IServerTransport<ISimpleNetworkConnection>>(
            provider =>
            {
                IOptions<TcpServerTransportOptions> options =
                    provider.GetRequiredService<IOptions<TcpServerTransportOptions>>();
                return new TcpServerTransport(options.Value);
            });
        return this;
    }

    /// <summary>Adds the tcp simple transport to the server builder.</summary>
    /// <param name="configure">Action to configure the provided <see cref="TcpServerTransportOptions"/>.</param>
    /// <returns>The server builder.</returns>
    public ServerBuilder UseTcp(Action<TcpServerTransportOptions> configure)
    {
        ServiceCollection.Configure(configure);
        return UseTcp();
    }

    /// <summary>Builds the server.</summary>
    /// <returns>The new server.</returns>
    public Server Build()
    {
        ServerOptions options = ServiceProvider.GetRequiredService<IOptions<ServerOptions>>().Value;
        Console.WriteLine($"build server endpoint: {options.Endpoint}");

        IServerTransport<ISimpleNetworkConnection>? simpleServerTransport =
            ServiceProvider.GetService<IServerTransport<ISimpleNetworkConnection>>();
        if (simpleServerTransport != null)
        {
            options.IceServerOptions ??= new IceServerOptions();
            options.IceServerOptions.ServerTransport = simpleServerTransport;
        }

        IServerTransport<IMultiplexedNetworkConnection>? multiplexedServerTransport =
            ServiceProvider.GetService<IServerTransport<IMultiplexedNetworkConnection>>();
        if (multiplexedServerTransport != null)
        {
            options.IceRpcServerOptions ??= new IceRpcServerOptions();
            options.IceRpcServerOptions.ServerTransport = multiplexedServerTransport;
        }

        options.LoggerFactory ??= ServiceProvider.GetService<ILoggerFactory>();

        options.AuthenticationOptions ??=
            ServiceProvider.GetService<IOptions<SslServerAuthenticationOptions>>()?.Value;

        options.Dispatcher =
            ServiceProvider.GetService<IDispatcher>() ?? ConnectionOptions.DefaultDispatcher;

        return new Server(options);
    }
}
