// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IceRpc.Extensions.DependencyInjection.Examples;

// This class provides code snippets used by the doc-comments of the AddIceRpcConnectionCache examples.
public static class AddIceRpcConnectionCacheExamples
{
    public static void AddDefaultConnectionCache(string[] args)
    {
        #region DefaultConnectionCache
        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services => services.AddIceRpcConnectionCache());
        #endregion
    }

    public static void AddConnectionCacheWithOptions(string[] args)
    {
        #region ConnectionCacheWithOptions
        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
        {
            services
                .AddOptions<ConnectionCacheOptions>()
                .Configure(options =>
                    options.ConnectTimeout = TimeSpan.FromSeconds(30));

            services.AddIceRpcConnectionCache();
        });
        #endregion
    }

    public static void AddConnectionCacheWithQuic(string[] args)
    {
        #region ConnectionCacheWithQuic
        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
            services
                // The IMultiplexedClientTransport singleton is implemented by QUIC.
                .AddSingleton<IMultiplexedClientTransport>(provider => new QuicClientTransport())
                .AddIceRpcConnectionCache());
        #endregion
    }
}
