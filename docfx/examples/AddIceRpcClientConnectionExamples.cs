// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;

// This class provides code snippets used by the doc-comments of the AddIceRpcClientConnection examples.
public static class AddIceRpcClientConnectionExamples
{
    public static void AddClientConnectionWithOptions()
    {
        #region ClientConnectionWithOptions
        var builder = Host.CreateDefaultBuilder(args);
        builder
            .AddOptions<ClientConnectionOptions>()
            // We need to set at least ServerAddress in the options.
            .Configure(options =>
                options.ServerAddress = new ServerAddress(new Uri("icerpc://localhost")));

        builder.ConfigureServices(services => services.AddIceRpcClientConnection());
        #endregion
    }

    public static void AddClientConnectionWithQuic()
    {
        #region ClientConnectionWithQuic
        var builder = Host.CreateDefaultBuilder(args);
        builder
            .AddOptions<ClientConnectionOptions>()
            // options.ClientAuthenticationOptions remains null which means we'll use the system certificates for this
            // secure QUIC connection.
            .Configure(options =>
                options.ServerAddress = new ServerAddress(new Uri("icerpc://localhost")));

        builder.ConfigureServices(services =>
            services
                // The IMultiplexedClientTransport singleton is implemented by QUIC.
                .AddSingleton<IMultiplexedClientTransport>(provider => new QuicClientTransport())
                .AddIceRpcClientConnection());
        #endregion
    }
}
