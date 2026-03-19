// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using IceRpc.Ice.Operations;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Ice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class IncomingResponseTests
{
    private static readonly IActivator _defaultActivator =
        IActivator.FromAssembly(typeof(IncomingResponseTests).Assembly);

    [Test]
    public void Decoded_dispatch_exception_from_incoming_void_response_has_convert_to_internal_error_set_to_true()
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
            async () => await response.DecodeVoidReturnValueAsync(request, InvalidProxy.Instance, _defaultActivator));
        Assert.That(decodedException, Is.Not.Null);
        Assert.That(decodedException!.ConvertToInternalError, Is.True);
    }

    [Test]
    public void Decoded_dispatch_exception_from_incoming_response_has_convert_to_internal_error_set_to_true()
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
            async () => await response.DecodeReturnValueAsync(
                request,
                InvalidProxy.Instance,
                (ref IceDecoder decoder) => decoder.DecodeInt(),
                _defaultActivator));
        Assert.That(decodedException, Is.Not.Null);
        Assert.That(decodedException!.ConvertToInternalError, Is.True);
    }
}
