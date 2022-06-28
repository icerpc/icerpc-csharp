// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace IceRpc.Bidir.Tests;

public sealed class BidirMiddlewareTests
{
    [Test]
    public async Task Bidir_connection_invoke_can_reconnect_after_connection_closed()
    {
        byte[] relativeOrigin = NewRelativeOrigin();

        // Create an incoming request that carries a relative origin and uses the closed connection.
        var connection1 = new ClosedConnection();
        var request1 = new IncomingRequest(connection1)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.RelativeOrigin] = new ReadOnlySequence<byte>(relativeOrigin),
            },
            Operation = "Op",
            Path = "/"
        };

        // A second request that carries the same relative origin and causes the reestablishment of the connection
        // which allows the bidir call to succeed.
        var connection2 = new OpenConnection();
        var request2 = new IncomingRequest(connection2)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.RelativeOrigin] = new ReadOnlySequence<byte>(relativeOrigin),
            },
            Operation = "Op",
            Path = "/"
        };

        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var sut = new BidirMiddleware(dispatcher, TimeSpan.FromSeconds(10));
        await sut.DispatchAsync(request1);

        var outgoingRequest = new OutgoingRequest(Proxy.FromConnection(request1.Connection, "/"));
        Task<IncomingResponse> invokeTask = request1.Connection.InvokeAsync(outgoingRequest, CancellationToken.None);
        await sut.DispatchAsync(request2);

        // Act
        var response = await invokeTask;

        Assert.That(response.Connection, Is.EqualTo(request1.Connection));
        Assert.That(connection1.InvokeCalled, Is.True);
        Assert.That(connection2.InvokeCalled, Is.True);
    }

    [Test]
    public async Task Bidir_connection_invoke_fails_after_reconnect_timeout()
    {
        byte[] relativeOrigin = NewRelativeOrigin();

        // Create an incoming request that carries a relative origin and uses the closed connection.
        var connection1 = new ClosedConnection();
        var request1 = new IncomingRequest(connection1)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.RelativeOrigin] = new ReadOnlySequence<byte>(relativeOrigin),
            },
            Operation = "Op",
            Path = "/"
        };

        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var sut = new BidirMiddleware(dispatcher, TimeSpan.FromMilliseconds(10));
        await sut.DispatchAsync(request1);

        var outgoingRequest = new OutgoingRequest(Proxy.FromConnection(request1.Connection, "/"));

        // Act/Assert
        Assert.That(
            async () => await request1.Connection.InvokeAsync(outgoingRequest, CancellationToken.None),
            Throws.TypeOf<ConnectionClosedException>());
    }

    private static byte[] NewRelativeOrigin()
    {
        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);

        byte[] relativeOrigin = new byte[16];
        using var provider = RandomNumberGenerator.Create();
        provider.GetBytes(relativeOrigin);

        encoder.EncodeSpan<byte>(relativeOrigin);
        pipe.Writer.Complete();

        pipe.Reader.TryRead(out ReadResult readResult);

        relativeOrigin = readResult.Buffer.ToArray();
        pipe.Reader.Complete();
        return relativeOrigin;
    }

    private class OpenConnection : IConnection
    {
        public bool InvokeCalled { get; private set; }

        public bool IsResumable => throw new NotImplementedException();

        public NetworkConnectionInformation? NetworkConnectionInformation => throw new NotImplementedException();

        public Protocol Protocol => Protocol.IceRpc;

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            InvokeCalled = true;
            return Task.FromResult(new IncomingResponse(request, this));
        }

        public void OnClose(Action<Exception> callback) => throw new NotImplementedException();
    }

    private class ClosedConnection : IConnection
    {
        public bool InvokeCalled { get; private set; }

        public bool IsResumable => throw new NotImplementedException();

        public NetworkConnectionInformation? NetworkConnectionInformation => throw new NotImplementedException();

        public Protocol Protocol => Protocol.IceRpc;

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            InvokeCalled = true;
            throw new ConnectionClosedException();
        }

        public void OnClose(Action<Exception> callback) => throw new NotImplementedException();
    }
}
