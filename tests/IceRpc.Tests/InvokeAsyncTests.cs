// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

public sealed class InvokeAsyncTests
{
    /// <summary>Verifies that <see cref="Connection.InvokeAsync(OutgoingRequest, CancellationToken)"/> can send and
    /// receive raw payload data without using Slice definitions.</summary>
    [Test]
    public async Task Invoke_can_send_and_receive_raw_payload_data()
    {
        // Arrange
        var colocTransport = new ColocTransport();

        await using var server = new Server(new ServerOptions()
        {
            Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    ReadResult readResult = await request.Payload.ReadAllAsync(cancel);
                    var responsePayload = new ReadOnlySequence<byte>(readResult.Buffer.ToArray());
                    await request.Payload.CompleteAsync(); // done with payload

                    var response = new OutgoingResponse(request)
                    {
                        PayloadSource = PipeReader.Create(responsePayload),
                    };
                    return response;
                }),
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport)
        });
        server.Listen();

        await using var connection = new Connection(new ConnectionOptions()
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport)
        });

        var requestPayload = new ReadOnlySequence<byte>(new byte[] { 0xAA, 0xBB, 0xCC });
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            PayloadSource = PipeReader.Create(requestPayload)
        };

        // Act
        IncomingResponse response = await connection.InvokeAsync(request, default);
        var responsePayload = (await response.Payload.ReadAllAsync(default)).Buffer.ToArray();
        await response.Payload.CompleteAsync(); // done with payload

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(responsePayload, Is.EqualTo(requestPayload.ToArray()));
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
        });
    }
}
