// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Slic;
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
                // options.ClientAuthenticationOptions remains null: this cache uses the Trusted Root CAs to validate
                // the server certificates when establishing secure QUIC connections.

            services.AddIceRpcConnectionCache();
        });
        #endregion
    }

    public static void AddConnectionCacheWithSlic(string[] args)
    {
        #region ConnectionCacheWithSlic
        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
            services
                // The IMultiplexedClientTransport singleton is implemented by Slic.
                .AddSingleton<IMultiplexedClientTransport>(
                    provider => new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>()))
                .AddIceRpcConnectionCache());
        #endregion
    }
}
