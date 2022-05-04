// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

public sealed class InvokeAsyncTests
{
    private static readonly byte[] _expectedPayload = { 0xAA, 0xBB, 0xCC };

    /// <summary>Verifies that sending a payload using
    /// <see cref="Connection.InvokeAsync(OutgoingRequest, CancellationToken)"/> works.</summary>
    /// <param name="endpoint">The server endpoint, used to specify the protocol to test.</param>
    [TestCase("icerpc://colochost")]
    [TestCase("ice://colochost")]
    public async Task Invoke_async_send_payload(string endpoint)
    {
        // Arrange
        var colocTransport = new ColocTransport();

        var serverOptions = CreateServerOptions(endpoint, colocTransport);

        byte[]? payload = null;
        serverOptions.Dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            ReadResult readResult = await request.Payload.ReadAllAsync(cancel);
            payload = readResult.Buffer.ToArray();
            return new OutgoingResponse(request);
        });

        await using var server = new Server(serverOptions);
        server.Listen();
        await using var connection = new Connection(CreateConnectionOptions(server.Endpoint, colocTransport));
        var proxy = Proxy.FromConnection(connection, "/name");

        var request = new OutgoingRequest(proxy)
        {
            Payload = PipeReader.Create(new ReadOnlySequence<byte>(_expectedPayload))
        };

        // Act
        IncomingResponse response = await proxy.Invoker.InvokeAsync(request, default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(payload, Is.EqualTo(_expectedPayload));
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
        });
    }

    /// <summary>Verifies that receiving a payload using
    /// <see cref="Connection.InvokeAsync(OutgoingRequest, CancellationToken)"/> works.</summary>
    /// <param name="endpoint">The server endpoint, used to specify the protocol to test.</param>
    [TestCase("icerpc://colochost")]
    [TestCase("ice://colochost")]
    public async Task Invoke_async_receive_payload(string endpoint)
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverOptions = CreateServerOptions(endpoint, colocTransport);
        serverOptions.Dispatcher = new InlineDispatcher((request, cancel) =>
            new(new OutgoingResponse(request)
            {
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(_expectedPayload)),
            }));

        await using var server = new Server(serverOptions);
        server.Listen();

        await using var connection = new Connection(CreateConnectionOptions(server.Endpoint, colocTransport));
        var proxy = Proxy.FromConnection(connection, "/name");
        var request = new OutgoingRequest(proxy);

        // Act
        IncomingResponse response = await proxy.Invoker.InvokeAsync(request, default);
        byte[] responsePayload = (await response.Payload.ReadAllAsync(default)).Buffer.ToArray();
        request.Complete();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(responsePayload, Is.EqualTo(_expectedPayload));
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
        });
    }

    private static ConnectionOptions CreateConnectionOptions(Endpoint remoteEndpoint, ColocTransport colocTransport)
    {
        var connectionOptions = new ConnectionOptions { RemoteEndpoint = remoteEndpoint };
        if (remoteEndpoint.Protocol == Protocol.Ice)
        {
            connectionOptions.IceClientOptions = new() { ClientTransport = colocTransport.ClientTransport };
        }
        else
        {
            connectionOptions.IceRpcClientOptions = new()
            {
                ClientTransport = new SlicClientTransport(colocTransport.ClientTransport)
            };
        }
        return connectionOptions;
    }

    private static ServerOptions CreateServerOptions(Endpoint endpoint, ColocTransport colocTransport)
    {
        var serverOptions = new ServerOptions { Endpoint = endpoint };
        if (endpoint.Protocol == Protocol.Ice)
        {
            serverOptions.IceServerOptions = new() { ServerTransport = colocTransport.ServerTransport };
        }
        else
        {
            serverOptions.IceRpcServerOptions = new()
            {
                ServerTransport = new SlicServerTransport(colocTransport.ServerTransport)
            };
        }
        return serverOptions;
    }
}
