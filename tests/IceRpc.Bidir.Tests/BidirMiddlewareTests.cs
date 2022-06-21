// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Bidir.Tests;

public sealed class BidirMiddlewareTests
{
    [Test]
    public async Task Bidir_midleware_decorates_the_request_connection()
    {
        // Arrange
        var connectionId = Guid.NewGuid();

        var connection1 = new InvalidConnection();

        // Create an incoming request that carries a connection ID
        var request = new IncomingRequest(connection1)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.ConnectionId] = EncodeConnectionId(connectionId),
            },
            Operation = "Op",
            Path = "/"
        };

        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var sut = new BidirMiddleware(dispatcher);

        // Act
        await sut.DispatchAsync(request);

        // Assert
        Assert.That(request.Connection, Is.TypeOf<Internal.BidirConnection>());
        var bidirConnection = (Internal.BidirConnection)request.Connection;
        Assert.That(bidirConnection.Decoratee, Is.EqualTo(connection1));
    }

    [Test]
    public async Task Bidir_midleware_updates_the_request_connection_decoratee()
    {
        // Arrange
        var connectionID = Guid.NewGuid();

        var connection1 = new InvalidConnection();
        var connection2 = new InvalidConnection();

        // Create an incoming request that carries a connection ID
        var request = new IncomingRequest(connection1)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.ConnectionId] = EncodeConnectionId(connectionID),
            },
            Operation = "Op",
            Path = "/"
        };

        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var sut = new BidirMiddleware(dispatcher);
        await sut.DispatchAsync(request);

        request = new IncomingRequest(connection2)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.ConnectionId] = EncodeConnectionId(connectionID),
            },
            Operation = "Op",
            Path = "/"
        };

        // Act
        await sut.DispatchAsync(request);

        // Assert
        Assert.That(request.Connection, Is.TypeOf<Internal.BidirConnection>());
        var bidirConnection = (Internal.BidirConnection)request.Connection;
        Assert.That(bidirConnection.Decoratee, Is.EqualTo(connection2));
    }

    private static ReadOnlySequence<byte> EncodeConnectionId(Guid connectionId)
    {
        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
        encoder.EncodeSequence(connectionId.ToByteArray());
        pipe.Writer.Complete();

        pipe.Reader.TryRead(out ReadResult readResult);
        var buffer = new ReadOnlySequence<byte>(readResult.Buffer.ToArray());
        pipe.Reader.Complete();
        return buffer;
    }

    private class InvalidConnection : IConnection
    {
        public bool IsResumable => throw new NotImplementedException();

        public NetworkConnectionInformation? NetworkConnectionInformation => throw new NotImplementedException();

        public Protocol Protocol => Protocol.IceRpc;

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
            throw new NotImplementedException();
        public void OnClose(Action<Exception> callback) => throw new NotImplementedException();
    }
}
