// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class IncomingResponseTests
{
    [Test]
    public void Decoded_dispatch_exception_from_unary_incoming_response_has_convert_to_internal_error_set_to_true()
    {
        // Arrange
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(
            request,
            FakeConnectionContext.Instance,
            StatusCode.DeadlineExceeded,
            "deadline expired");

        // Act/Assert
        DispatchException? decodedException = Assert.ThrowsAsync<DispatchException>(
            async () => await InvokerExtensions.ReceiveResponseAsync(
                Empty.Parser,
                Task.FromResult(response),
                request,
                default));
        Assert.That(decodedException, Is.Not.Null);
        Assert.That(decodedException!.ConvertToInternalError, Is.True);
    }

    [Test]
    public void Decoded_dispatch_exception_from_streaming_incoming_response_has_convert_to_internal_error_set_to_true()
    {
        // Arrange
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(
            request,
            FakeConnectionContext.Instance,
            StatusCode.DeadlineExceeded,
            "deadline expired");

        // Act/Assert
        DispatchException? decodedException = Assert.ThrowsAsync<DispatchException>(
            async () => await InvokerExtensions.ReceiveStreamingResponseAsync(
                Empty.Parser,
                Task.FromResult(response),
                request,
                default));
        Assert.That(decodedException, Is.Not.Null);
        Assert.That(decodedException!.ConvertToInternalError, Is.True);
    }
}
