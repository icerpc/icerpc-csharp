// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

public sealed class InvokeAsyncTests
{
    /// <summary>Verifies that sending a payload using
    /// <see cref="Connection.InvokeAsync(OutgoingRequest, CancellationToken)"/> works.</summary>
    [Test]
    public async Task Invoke_async_send_payload()
    {
        // Arrange
        var colocTransport = new ColocTransport();

        byte[]? payload = null;
        byte[] expectedPayload = new byte[] { 0xAA, 0xBB, 0xCC };
        await using var server = new Server(new ServerOptions()
        {
            Dispatcher = new InlineDispatcher(async (request, cancel) =>
            {
                ReadResult readResult = await request.Payload.ReadAllAsync(cancel);
                payload = readResult.Buffer.ToArray();
                await request.Payload.CompleteAsync(); // done with payload
                return new OutgoingResponse(request);
            }),
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport)
        });
        server.Listen();

        await using var connection = new Connection(new ConnectionOptions()
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport)
        });
        var proxy = Proxy.FromConnection(connection, "/");

        var request = new OutgoingRequest(proxy)
        {
            PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
        };

        // Act
        IncomingResponse response = await proxy.Invoker.InvokeAsync(request, default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(payload, Is.EqualTo(expectedPayload));
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
        });
    }

    /// <summary>Verifies that receiving a payload using
    /// <see cref="Connection.InvokeAsync(OutgoingRequest, CancellationToken)"/> works.</summary>
    [Test]
    public async Task Invoke_async_receive_payload()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        byte[] expectedPayload = new byte[] { 0xAA, 0xBB, 0xCC };

        await using var server = new Server(new ServerOptions()
        {
            Dispatcher = new InlineDispatcher(async (request, cancel) =>
            {
                _ = await request.Payload.ReadAllAsync(cancel);
                return new OutgoingResponse(request)
                {
                    PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload)),
                };
            }),
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport)
        });
        server.Listen();

        await using var connection = new Connection(new ConnectionOptions()
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport)
        });
        var proxy = Proxy.FromConnection(connection, "/");

        // Act
        IncomingResponse response = await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy), default);
        byte[] responsePayload = (await response.Payload.ReadAllAsync(default)).Buffer.ToArray();
        await response.Payload.CompleteAsync(); // done with payload

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(responsePayload, Is.EqualTo(expectedPayload));
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
        });
    }
}
