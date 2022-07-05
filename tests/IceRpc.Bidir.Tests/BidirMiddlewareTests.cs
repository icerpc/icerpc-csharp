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
    public async Task Bidir_middleware_updates_the_bidir_connection_decoratee_on_dispatch()
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

        // A second request that carries the same relative origin and triggers the updated of the
        // bidir connection decoratee
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

        var outgoingRequest = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/" });
        await sut.DispatchAsync(request2);
        Task<IncomingResponse> invokeTask = request1.Connection.InvokeAsync(outgoingRequest, CancellationToken.None);

        // Act
        var response = await invokeTask;

        // Assert
        Assert.That(response.Connection, Is.EqualTo(request1.Connection));
        Assert.That(connection1.InvokeCalled, Is.False);
        Assert.That(connection2.InvokeCalled, Is.True);
    }

    [Test]
    public async Task Bidir_middleware_removes_the_bidir_connection_on_decoratee_shutdown()
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

        // A second request that carries the same relative origin and triggers the updated of the
        // bidir connection decoratee
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

        // Triggers the instant removal of the connection from the bidir middleware.
        connection1.InvokeOnShutdown();

        var outgoingRequest = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/" });
        await sut.DispatchAsync(request2);
        Task<IncomingResponse> invokeTask = request1.Connection.InvokeAsync(outgoingRequest, CancellationToken.None);

        // Act/Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionClosedException>());
        Assert.That(connection1.InvokeCalled, Is.True);
        Assert.That(connection2.InvokeCalled, Is.False);
    }

    [Test]
    public async Task Bidir_middleware_removes_bidir_connection_on_abort_after_reconnect_timeout()
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

        // A second request that carries the same relative origin and triggers the updated of the
        // bidir connection decoratee
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
        var sut = new BidirMiddleware(dispatcher, TimeSpan.FromMilliseconds(10));
        await sut.DispatchAsync(request1);

        // Triggers the removal of the connection within the reconnect timeout from the bidir middleware.
        connection1.InvokeOnAbort();

        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var outgoingRequest = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/" });
        await sut.DispatchAsync(request2);
        Task<IncomingResponse> invokeTask = request1.Connection.InvokeAsync(outgoingRequest, CancellationToken.None);

        // Act/Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionAbortedException>());
        Assert.That(connection1.InvokeCalled, Is.False);
        Assert.That(connection2.InvokeCalled, Is.False);
    }

    [Test]
    public async Task Bidir_middleware_waits_for_bidir_connection_update_on_abort()
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

        // A second request that carries the same relative origin and triggers the updated of the
        // bidir connection decoratee
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

        // Triggers the removal of the connection within the reconnect timeout from the bidir middleware.
        connection1.InvokeOnAbort();

        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var outgoingRequest = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/" });
        await sut.DispatchAsync(request2);
        Task<IncomingResponse> invokeTask = request1.Connection.InvokeAsync(outgoingRequest, CancellationToken.None);

        // Act
        var response = await invokeTask;

        // Assert
        Assert.That(response.Connection, Is.EqualTo(request1.Connection));
        Assert.That(connection1.InvokeCalled, Is.False);
        Assert.That(connection2.InvokeCalled, Is.True);
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

        public void OnAbort(Action<Exception> callback)
        {
        }

        public void OnShutdown(Action<string> callback)
        {
        }
    }

    private class ClosedConnection : IConnection
    {
        Action<Exception>? _onAbort;
        Action<string>? _onShutdown;

        public bool InvokeCalled { get; private set; }

        public bool IsResumable => throw new NotImplementedException();

        public NetworkConnectionInformation? NetworkConnectionInformation => throw new NotImplementedException();

        public Protocol Protocol => Protocol.IceRpc;

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            InvokeCalled = true;
            throw new ConnectionClosedException();
        }

        public void OnAbort(Action<Exception> onAbort) => _onAbort = onAbort;

        public void OnShutdown(Action<string> onShutdown) => _onShutdown = onShutdown;

        internal void InvokeOnAbort() => _onAbort?.Invoke(new ConnectionAbortedException());

        internal void InvokeOnShutdown() => _onShutdown?.Invoke("connection closed");
    }
}
