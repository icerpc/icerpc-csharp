// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Tests.Transports.Slic;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Tests;

public static class ProtocolServiceCollectionExtensions
{
    /// <summary>Adds a ClientServerProtocolConnection singleton for use by protocol tests.</summary>
    public static IServiceCollection AddProtocolTest(
        this IServiceCollection services,
        Protocol protocol,
        IDispatcher? dispatcher = null,
        ConnectionOptions? clientConnectionOptions = null,
        ConnectionOptions? serverConnectionOptions = null)
    {
        clientConnectionOptions ??= new();
        clientConnectionOptions.Dispatcher ??= NotFoundDispatcher.Instance;
        serverConnectionOptions ??= new();
        serverConnectionOptions.Dispatcher ??= dispatcher ?? NotFoundDispatcher.Instance;

        if (protocol == Protocol.Ice)
        {
            services
                .AddColocTransport()
                .AddDuplexTransportTest()
                .AddSingleton(provider =>
                    new ClientServerProtocolConnection(
                        clientProtocolConnection: new IceProtocolConnection(
                            provider.GetRequiredService<ClientServerDuplexConnection>().Client,
                            transportConnectionInformation: null,
                            clientConnectionOptions ?? new()),
                        acceptServerConnectionAsync:
                            async (CancellationToken cancellationToken) =>
                            {
                                ClientServerDuplexConnection clientServerConnection =
                                    provider.GetRequiredService<ClientServerDuplexConnection>();

                                TransportConnectionInformation transportConnectionInformation =
                                    await clientServerConnection.AcceptAsync(cancellationToken);

                                return new IceProtocolConnection(
                                    clientServerConnection.Server,
                                    transportConnectionInformation,
                                    serverConnectionOptions ?? new());
                            }));
        }
        else
        {
            services
                .AddColocTransport()
                .AddSlicTransport()
                .AddMultiplexedTransportTest()
                .AddSingleton(provider =>
                    new ClientServerProtocolConnection(
                        clientProtocolConnection: new IceRpcProtocolConnection(
                            provider.GetRequiredService<ClientServerMultiplexedConnection>().Client,
                            transportConnectionInformation: null,
                            clientConnectionOptions ?? new(),
                            provider.GetService<ITaskExceptionObserver>()),
                        acceptServerConnectionAsync:
                            async (CancellationToken cancellationToken) =>
                            {
                                ClientServerMultiplexedConnection clientServerConnection =
                                    provider.GetRequiredService<ClientServerMultiplexedConnection>();

                                TransportConnectionInformation transportConnectionInformation =
                                    await clientServerConnection.AcceptAsync(cancellationToken);

                                return new IceRpcProtocolConnection(
                                    clientServerConnection.Server,
                                    transportConnectionInformation,
                                    serverConnectionOptions ?? new(),
                                    provider.GetService<ITaskExceptionObserver>());
                            }));
        }
        return services;
    }
}
