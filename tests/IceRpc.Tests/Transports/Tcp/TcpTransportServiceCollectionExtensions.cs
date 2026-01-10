// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Tests.Transports.Tcp;

internal static class TcpTransportServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        internal IServiceCollection AddTcpTest(
            int? listenBacklog,
            Uri? serverAddressUri = null) => services
            .AddDuplexTransportTest(serverAddressUri ?? new Uri("icerpc://127.0.0.1:0/"))
            .AddTcpTransport()
            .AddSingleton<TcpServerTransportOptions>(
                _ => listenBacklog is null ? new() : new() { ListenBacklog = listenBacklog.Value });

        internal IServiceCollection AddTcpTransport() =>
            services
                .AddSingleton<TcpClientTransportOptions>()
                .AddSingleton<TcpServerTransportOptions>()
                .AddSingleton<IDuplexServerTransport>(
                    provider => new TcpServerTransport(provider.GetRequiredService<TcpServerTransportOptions>()))
                .AddSingleton<IDuplexClientTransport>(
                    provider => new TcpClientTransport(provider.GetRequiredService<TcpClientTransportOptions>()));
    }
}
