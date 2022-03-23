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
            await request.Payload.CompleteAsync(); // done with payload
            return new OutgoingResponse(request);
        });

        await using var server = new Server(serverOptions);
        server.Listen();
        await using var connection = new Connection(CreateConnectionOptions(server.Endpoint, colocTransport));
        var proxy = Proxy.FromConnection(connection, "/name");

        var request = new OutgoingRequest(proxy)
        {
            PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(_expectedPayload))
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
        serverOptions.Dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            await request.Payload.CompleteAsync();
            return new OutgoingResponse(request)
            {
                PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(_expectedPayload)),
            };
        });

        await using var server = new Server(serverOptions);
        server.Listen();

        await using var connection = new Connection(CreateConnectionOptions(server.Endpoint, colocTransport));
        var proxy = Proxy.FromConnection(connection, "/name");

        // Act
        IncomingResponse response = await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy), default);
        byte[] responsePayload = (await response.Payload.ReadAllAsync(default)).Buffer.ToArray();
        await response.Payload.CompleteAsync(); // done with payload

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
            connectionOptions.SimpleClientTransport = colocTransport.ClientTransport;
        }
        else
        {
            connectionOptions.MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport);
        }
        return connectionOptions;
    }

    private static ServerOptions CreateServerOptions(Endpoint endpoint, ColocTransport colocTransport)
    {
        var serverOptions = new ServerOptions { Endpoint = endpoint };
        if (endpoint.Protocol == Protocol.Ice)
        {
            serverOptions.SimpleServerTransport = colocTransport.ServerTransport;
        }
        else
        {
            serverOptions.MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport);
        }
        return serverOptions;
    }
}
