// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IceRpc.Extensions.DependencyInjection.Examples;

// This class provides code snippets used by the doc-comments of the AddIceRpcClientConnection examples.
public static class AddIceRpcClientConnectionExamples
{
    public static void AddClientConnectionWithOptions(string[] args)
    {
        #region ClientConnectionWithOptions
        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
        {
            services
                .AddOptions<ClientConnectionOptions>()
                // We need to set at least ServerAddress in the options.
                .Configure(options =>
                    options.ServerAddress = new ServerAddress(new Uri("icerpc://localhost")));

            services.AddIceRpcClientConnection();
        });
        #endregion
    }

    public static void AddClientConnectionWithQuic(string[] args)
    {
        #region ClientConnectionWithQuic
        IHostBuilder builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
        {
            services
                .AddOptions<ClientConnectionOptions>()
                // options.ClientAuthenticationOptions remains null which means we'll use the system certificates for this
                // secure QUIC connection.
                .Configure(options =>
                    options.ServerAddress = new ServerAddress(new Uri("icerpc://localhost")));
            services
                // The IMultiplexedClientTransport singleton is implemented by QUIC.
                .AddSingleton<IMultiplexedClientTransport>(provider => new QuicClientTransport())
                .AddIceRpcClientConnection();
        });
        #endregion
    }
}
