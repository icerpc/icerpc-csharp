// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests
{
    /// <summary>A helper struct to ensure the network and protocol connections are correctly disposed.</summary>
    internal struct ClientServerProtocolConnection : IAsyncDisposable
    {
        internal IProtocolConnection Client { get; }
        internal INetworkConnection ClientNetworkConnection { get; }
        internal IProtocolConnection Server { get; }
        internal INetworkConnection ServerNetworkConnection { get; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Server.Dispose();
            await ClientNetworkConnection.DisposeAsync();
            await ServerNetworkConnection.DisposeAsync();
        }

        internal ClientServerProtocolConnection(
            INetworkConnection clientNetworkConnection,
            INetworkConnection serverNetworkConnection,
            IProtocolConnection clientConnection,
            IProtocolConnection serverConnection)
        {
            ClientNetworkConnection = clientNetworkConnection;
            ServerNetworkConnection = serverNetworkConnection;
            Client = clientConnection;
            Server = serverConnection;
        }
    }

    internal class ProtocolServiceCollection : TransportServiceCollection
    {
        public ProtocolServiceCollection()
        {
            this.AddSingleton(IceProtocol.Instance.ProtocolConnectionFactory);
            this.AddSingleton(IceRpcProtocol.Instance.ProtocolConnectionFactory);
        }
    }

    internal static class ProtocolServiceCollectionExtensions
    {
        internal static IServiceCollection UseProtocol(this IServiceCollection collection, Protocol protocol) =>
            collection.AddSingleton(protocol);

        internal static IServiceCollection UseServerConnectionOptions(
            this IServiceCollection collection,
            ConnectionOptions options) =>
            collection.AddSingleton(new ServerConnectionOptions(options));

        internal static IServiceCollection UseClientConnectionOptions(
            this IServiceCollection collection,
            ConnectionOptions options) =>
            collection.AddSingleton(new ClientConnectionOptions(options));

        internal static async Task<ClientServerProtocolConnection> GetClientServerProtocolConnectionAsync(
            this IServiceProvider serviceProvider)
        {
            Task<(INetworkConnection, IProtocolConnection)> serverTask =
                serviceProvider.GetServerProtocolConnectionAsync();
            (INetworkConnection clientNetworkConnection, IProtocolConnection clientProtocolConnection) =
                await serviceProvider.GetClientProtocolConnectionAsync();
            (INetworkConnection serverNetworkConnection, IProtocolConnection serverProtocolConnection) =
                await serverTask;
            return new ClientServerProtocolConnection(
                clientNetworkConnection,
                serverNetworkConnection,
                clientProtocolConnection,
                serverProtocolConnection);
        }

        private static Task<(INetworkConnection, IProtocolConnection)> GetClientProtocolConnectionAsync(
            this IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
                GetProtocolConnectionAsync(
                    serviceProvider,
                    Protocol.Ice,
                    isServer: false,
                    serviceProvider.GetSimpleClientConnectionAsync) :
                GetProtocolConnectionAsync(
                    serviceProvider,
                    Protocol.IceRpc,
                    isServer: false,
                    serviceProvider.GetMultiplexedClientConnectionAsync);

        private static async Task<(INetworkConnection, IProtocolConnection)> GetProtocolConnectionAsync<T>(
            IServiceProvider serviceProvider,
            Protocol protocol,
            bool isServer,
            Func<Task<T>> networkConnectionFactory) where T : INetworkConnection
        {
            T networkConnection = await networkConnectionFactory();
            Connection connection = protocol == Protocol.Ice ? InvalidConnection.Ice : InvalidConnection.IceRpc;
            IProtocolConnection protocolConnection =
                await serviceProvider.GetRequiredService<IProtocolConnectionFactory<T>>().CreateProtocolConnectionAsync(
                    networkConnection,
                    connectionInformation: new(),
                    CreateIncomingRequest,
                    CreateIncomingResponse,
                    connectionOptions: isServer ?
                        serviceProvider.GetService<ServerConnectionOptions>()?.Value ?? new() :
                        serviceProvider.GetService<ClientConnectionOptions>()?.Value ?? new(),
                    isServer,
                    CancellationToken.None);
            return (networkConnection, protocolConnection);

            IncomingRequest CreateIncomingRequest(
                FeatureCollection features,
                IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields,
                PipeReader? fieldsPipeReader,
                string fragment,
                bool oneway,
                string operation,
                string path,
                PipeReader payload) =>
                new(connection, fields, fieldsPipeReader)
                {
                    Features = features,
                    Fragment = fragment,
                    IsOneway = oneway,
                    Operation = operation,
                    Path = path,
                    Payload = payload
                };

            IncomingResponse CreateIncomingResponse(
                OutgoingRequest request,
                IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields,
                PipeReader? fieldsPipeReader,
                PipeReader payload,
                ResultType resultType) =>
                new(request, connection, fields, fieldsPipeReader)
                {
                    Payload = payload,
                    ResultType = resultType
                };
        }

        private static Task<(INetworkConnection, IProtocolConnection)> GetServerProtocolConnectionAsync(
            this IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
                GetProtocolConnectionAsync(
                    serviceProvider,
                    Protocol.Ice,
                    isServer: true,
                    serviceProvider.GetSimpleServerConnectionAsync) :
                GetProtocolConnectionAsync(
                    serviceProvider,
                    Protocol.IceRpc,
                    isServer: true,
                    serviceProvider.GetMultiplexedServerConnectionAsync);

        private sealed class ClientConnectionOptions
        {
            internal ConnectionOptions Value { get; }

            internal ClientConnectionOptions(ConnectionOptions options) => Value = options;
        }

        private sealed class ServerConnectionOptions
        {
            internal ConnectionOptions Value { get; }

            internal ServerConnectionOptions(ConnectionOptions options) => Value = options;
        }
    }
}
