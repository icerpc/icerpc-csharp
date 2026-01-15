// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using ZeroC.Slice;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class InvokeOperationAsyncTests
{
    /// <summary>Verifies that InvokeOperationAsync completes the outgoing request and incoming response payloads.</summary>
    [Test]
    public async Task InvokeOperationAsync_completes_all_payloads()
    {
        var responsePayload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);

        var sut = new PingableProxy
        {
            ServiceAddress = new ServiceAddress(Protocol.IceRpc),
            Invoker = new InlineInvoker((request, cancellationToken) =>
                Task.FromResult(
                    new IncomingResponse(request, FakeConnectionContext.Instance) { Payload = responsePayload }))
        };

        var requestPayload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);

        // Act
        await sut.InvokeOperationAsync(
            "",
            payload: requestPayload,
            payloadContinuation: null,
            responseDecodeFunc: (response, request, sender, cancellationToken) =>
                response.DecodeVoidReturnValueAsync(
                    request,
                    SliceEncoding.Slice2,
                    InvalidProxy.Instance,
                    cancellationToken: cancellationToken),
            features: null);

        // Assert
        Assert.That(requestPayload.Completed.IsCompleted, Is.True);
        Assert.That(responsePayload.Completed.IsCompleted, Is.True);
        Assert.That(await requestPayload.Completed, Is.Null);
        Assert.That(await responsePayload.Completed, Is.Null);
    }

    /// <summary>Verifies that InvokeOperationAsync completes the request payload and payload continuation when an exception is thrown
    /// "on the way out".</summary>
    [Test]
    public void InvokeOperationAsync_completes_all_payloads_on_outgoing_exception()
    {
        var sut = new PingableProxy
        {
            ServiceAddress = new ServiceAddress(Protocol.IceRpc),
            Invoker = new InlineInvoker((request, cancellationToken) => throw new InvalidDataException("error"))
        };

        var requestPayload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var requestPayloadContinuation = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);

        // Act/Assert
        Assert.That(
            async () => await sut.InvokeOperationAsync(
                "",
                payload: requestPayload,
                payloadContinuation: requestPayloadContinuation,
                responseDecodeFunc: (response, request, sender, cancellationToken) =>
                    response.DecodeVoidReturnValueAsync(
                        request,
                        SliceEncoding.Slice2,
                        InvalidProxy.Instance,
                        cancellationToken: cancellationToken),
                features: null),
            Throws.InstanceOf<InvalidDataException>());

        Assert.That(requestPayload.Completed.IsCompletedSuccessfully, Is.True);
        Assert.That(requestPayloadContinuation.Completed.IsCompletedSuccessfully, Is.True);
    }
}
