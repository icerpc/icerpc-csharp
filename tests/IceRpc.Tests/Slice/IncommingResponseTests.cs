// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class IncomingResponseTests
{
    [Test]
    public void Decoded_dispatch_exception_from_incoming_void_response_has_convert_to_unhandled_set_to_true()
    {
        // Arrange
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(
            request,
            FakeConnectionContext.Instance,
            StatusCode.DeadlineExpired,
            "deadline expired");

        // Act/Assert
        DispatchException? decodedException = Assert.ThrowsAsync<DispatchException>(
            async () => await response.DecodeVoidReturnValueAsync(
                request,
                SliceEncoding.Slice2,
                InvalidProxy.Instance));
        Assert.That(decodedException, Is.Not.Null);
        Assert.That(decodedException!.ConvertToUnhandled, Is.True);
    }

    [Test]
    public void Decoded_dispatch_exception_from_incoming_response_has_convert_to_unhandled_set_to_true()
    {
        // Arrange
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(
            request,
            FakeConnectionContext.Instance,
            StatusCode.DeadlineExpired,
            "deadline expired");

        // Act/Assert
        DispatchException? decodedException = Assert.ThrowsAsync<DispatchException>(
            async () => await response.DecodeReturnValueAsync(
                request,
                SliceEncoding.Slice2,
                InvalidProxy.Instance,
                (ref SliceDecoder decoder) => decoder.DecodeInt32()));
        Assert.That(decodedException, Is.Not.Null);
        Assert.That(decodedException!.ConvertToUnhandled, Is.True);
    }
}
