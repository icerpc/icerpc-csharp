// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

public sealed class InvokeAsyncTests
{
    /// <summary>Verifies that we can send a receive a payload using a custom encoding.</summary>
    [Test]
    public async Task Invoke_with_custom_encoding()
    {
        // Arrange
        var encoding = Encoding.FromString("utf8");
        string greeting = "how are you doing?";
        string doingWell = "well";
        var payload = new ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(greeting));
        var proxy = new Proxy(Protocol.IceRpc);
        var request = new OutgoingRequest(proxy)
        {
            PayloadEncoding = encoding,
            PayloadSource = PipeReader.Create(payload)
        };
        Encoding? requestPayloadEncoding = null;
        string? greetingRequest = null;

        var colocTransport = new ColocTransport();

        await using var server = new Server(new ServerOptions()
        {
            Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    requestPayloadEncoding = request.PayloadEncoding;

                    greetingRequest = System.Text.Encoding.UTF8.GetString(
                        (await ReadFullPayloadAsync(request.Payload, cancel)).Span);
                    await request.Payload.CompleteAsync(); // done with payload


                    var payload = new ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(doingWell));
                    var response = new OutgoingResponse(request)
                    {
                        PayloadSource = PipeReader.Create(payload),
                        ResultType = ResultType.Success
                        // use same payload encoding as request (default)
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

        // Act
        IncomingResponse response = await connection.InvokeAsync(request, default);
        string greetingResponse = System.Text.Encoding.UTF8.GetString(
            (await ReadFullPayloadAsync(response.Payload)).Span);
        await response.Payload.CompleteAsync(); // done with payload

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(requestPayloadEncoding, Is.EqualTo(encoding));
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
            Assert.That(greetingRequest, Is.EqualTo(greeting));
            Assert.That(greetingResponse, Is.EqualTo(doingWell));
        });
    }

    private static async ValueTask<ReadOnlyMemory<byte>> ReadFullPayloadAsync(
        PipeReader reader,
        CancellationToken cancel = default)
    {
        ReadResult readResult = await reader.ReadAllAsync(cancel);

        Assert.That(readResult.Buffer.IsSingleSegment); // very likely; if not, fix test
        return readResult.Buffer.First;
    }
}
