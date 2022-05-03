// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
[Timeout(30000)]
public class InvokeAsyncTests
{
    /// <summary>Verifies that InvokeAsync completes the outgoing request and incoming response payloads.</summary>
    [Test]
    public async Task InvokeAsync_completes_all_payloads()
    {
        var responsePayload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);

        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker((request, cancel) =>
                Task.FromResult(new IncomingResponse(request, InvalidConnection.IceRpc) { Payload = responsePayload }))
        };

        var requestPayload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);

        // Act
        await proxy.InvokeAsync(
            "",
            SliceEncoding.Slice2,
            payload: requestPayload,
            payloadStream: null,
            defaultActivator: null,
            invocation: null);

        // Assert
        Assert.Multiple(async () =>
        {
            Assert.That(requestPayload.Completed.IsCompleted, Is.True);
            Assert.That(responsePayload.Completed.IsCompleted, Is.True);
            Assert.That(await requestPayload.Completed, Is.Null);
            Assert.That(await responsePayload.Completed, Is.Null);
        });
    }

    /// <summary>Verifies that InvokeAsync completes the request payload and payload stream when an exception is thrown
    /// "on the way out".</summary>
    [Test]
    public void InvokeAsync_completes_all_payloads_on_outgoing_exception()
    {
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker((request, cancel) => throw new InvalidDataException("error"))
        };

        var requestPayload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var requestPayloadStream = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);

        // Act/Assert
        Assert.Multiple(async () =>
        {
            Assert.That(
                async () => await proxy.InvokeAsync(
                    "",
                    SliceEncoding.Slice2,
                    payload: requestPayload,
                    payloadStream: requestPayloadStream,
                    defaultActivator: null,
                    invocation: null),
                Throws.InstanceOf<InvalidDataException>());

            Assert.That(requestPayload.Completed.IsCompleted, Is.True);
            Assert.That(requestPayloadStream.Completed.IsCompleted, Is.True);
            Assert.That(await requestPayload.Completed, Is.TypeOf<InvalidDataException>());
            Assert.That(await requestPayloadStream.Completed, Is.TypeOf<InvalidDataException>());
        });
    }
}
