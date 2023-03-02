// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Tests.Transports;
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
        clientConnectionOptions.Dispatcher ??= ServiceNotFoundDispatcher.Instance;
        serverConnectionOptions ??= new();
        serverConnectionOptions.Dispatcher ??= dispatcher ?? ServiceNotFoundDispatcher.Instance;

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
                                IDuplexConnection transportConnection;
                                TransportConnectionInformation transportConnectionInformation;
                                (transportConnection, transportConnectionInformation) =
                                    await provider.GetRequiredService<ClientServerDuplexConnection>().AcceptAsync(
                                        cancellationToken);

                                return new IceProtocolConnection(
                                    transportConnection,
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
                                IMultiplexedConnection transportConnection;
                                TransportConnectionInformation transportConnectionInformation;
                                (transportConnection, transportConnectionInformation) =
                                    await provider.GetRequiredService<ClientServerMultiplexedConnection>().AcceptAsync(
                                        cancellationToken);

                                return new IceRpcProtocolConnection(
                                    transportConnection,
                                    transportConnectionInformation,
                                    serverConnectionOptions ?? new(),
                                    provider.GetService<ITaskExceptionObserver>());
                            }));
        }
        return services;
    }
}
