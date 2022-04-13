// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class OperationTests
{
    [Test]
    public async Task Operation_encode_decode_with_single_parameter()
    {
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = MyOperationsPrx.Request.OpInt(10)
        };

        int value = await IMyOperations.Request.OpIntAsync(request, default);

        Assert.That(value, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_encode_decode_with_single_return()
    {
        await using var connection = new Connection(new ConnectionOptions());
        var request = new OutgoingResponse(new IncomingRequest(Protocol.IceRpc))
        {
            Payload = IMyOperations.Response.OpInt(10)
        };

        var response = new IncomingResponse(new OutgoingRequest(new Proxy(Protocol.IceRpc)))
        {
            Connection = connection,
            Payload = request.Payload
        };

        int value = await MyOperationsPrx.Response.OpIntAsync(response, default);

        Assert.That(value, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_encode_decode_with_multiple_parameters()
    {
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = MyOperationsPrx.Request.OpIntAndString(10, "hello world!")
        };

        var value = await IMyOperations.Request.OpIntAndStringAsync(request, default);

        Assert.That(value.P1, Is.EqualTo(10));
        Assert.That(value.P2, Is.EqualTo("hello world!"));
    }

    [Test]
    public async Task Operation_encode_decode_with_multiple_return()
    {
        await using var connection = new Connection(new ConnectionOptions());
        var request = new OutgoingResponse(new IncomingRequest(Protocol.IceRpc))
        {
            Payload = IMyOperations.Response.OpIntAndString(10, "hello world!")
        };

        var response = new IncomingResponse(new OutgoingRequest(new Proxy(Protocol.IceRpc)))
        {
            Connection = connection,
            Payload = request.Payload
        };

        var value = await MyOperationsPrx.Response.OpIntAndStringAsync(response, default);

        Assert.That(value.R1, Is.EqualTo(10));
        Assert.That(value.R2, Is.EqualTo("hello world!"));
    }

    [Test]
    public async Task Operation_encode_decode_with_optional_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = MyOperationsPrx.Request.OpOptional(p1, p2, p3, p4)
        };

        var value = await IMyOperations.Request.OpOptionalAsync(request, default);

        Assert.That(value.P1, Is.EqualTo(p1));
        Assert.That(value.P2, Is.EqualTo(p2));
        Assert.That(value.P3, Is.EqualTo(p3));
        Assert.That(value.P4, Is.EqualTo(p4));
    }

    [Test]
    public async Task Operation_encode_decode_with_optional_return(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        await using var connection = new Connection(new ConnectionOptions());
        var request = new OutgoingResponse(new IncomingRequest(Protocol.IceRpc))
        {
            Payload = IMyOperations.Response.OpOptional(p1, p2, p3, p4)
        };

        var response = new IncomingResponse(new OutgoingRequest(new Proxy(Protocol.IceRpc)))
        {
            Connection = connection,
            Payload = request.Payload
        };

        var value = await MyOperationsPrx.Response.OpOptionalAsync(response, default);

        Assert.That(value.R1, Is.EqualTo(p1));
        Assert.That(value.R2, Is.EqualTo(p2));
        Assert.That(value.R3, Is.EqualTo(p3));
        Assert.That(value.R4, Is.EqualTo(p4));
    }

    [Test]
    public async Task Operation_encode_decode_with_tagged_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = MyOperationsPrx.Request.OpTagged(p1, p2, p3, p4)
        };

        var value = await IMyOperations.Request.OpTaggedAsync(request, default);

        Assert.That(value.P1, Is.EqualTo(p1));
        Assert.That(value.P2, Is.EqualTo(p2));
        Assert.That(value.P3, Is.EqualTo(p3));
        Assert.That(value.P4, Is.EqualTo(p4));
    }

    [Test]
    public async Task Operation_encode_decode_with_tagged_return(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        await using var connection = new Connection(new ConnectionOptions());
        var request = new OutgoingResponse(new IncomingRequest(Protocol.IceRpc))
        {
            Payload = IMyOperations.Response.OpTagged(p1, p2, p3, p4)
        };

        var response = new IncomingResponse(new OutgoingRequest(new Proxy(Protocol.IceRpc)))
        {
            Connection = connection,
            Payload = request.Payload
        };

        var value = await MyOperationsPrx.Response.OpTaggedAsync(response, default);

        Assert.That(value.R1, Is.EqualTo(p1));
        Assert.That(value.R2, Is.EqualTo(p2));
        Assert.That(value.R3, Is.EqualTo(p3));
        Assert.That(value.R4, Is.EqualTo(p4));
    }
}
