// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;

// This class provides code snippets used by the doc-comments of the AddIceRpcConnectionCache examples.
public static class AddIceRpcConnectionCacheExamples
{
    public static void AddDefaultConnectionCache()
    {
        #region DefaultConnectionCache
        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services => services.AddIceRpcConnectionCache());
        #endregion
    }

    public static void AddConnectionCacheWithOptions()
    {
        #region ConnectionCacheWithOptions
        var builder = Host.CreateDefaultBuilder(args);
        builder
            .AddOptions<ConnectionCacheOptions>()
            .Configure(options =>
                options.ConnectionTimeout = TimeSpan.FromSeconds(30));

        builder.ConfigureServices(services => services.AddIceRpcConnectionCache());
        #endregion
    }

    public static void AddConnectionCacheWithQuic()
    {
        #region ConnectionCacheWithQuic
        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
            services
                // The IMultiplexedClientTransport singleton is implemented by QUIC.
                .AddSingleton<IMultiplexedClientTransport>(provider => new QuicClientTransport())
                .AddIceRpcConnectionCache());
        #endregion
    }
}
