// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
[Timeout(5000)]
public class InvocationTests
{
    /// <summary>Verifies that the request context feature is set to the invocation context.</summary>
    [Test]
    public async Task Invocation_context()
    {
        // Arrange
        var invocation = new Invocation
        {
            Features = new FeatureCollection().WithContext(new Dictionary<string, string> { ["foo"] = "bar" })
        };

        IDictionary<string, string>? context = null;
        var sut = new ServicePrx(new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker((request, cancel) =>
            {
                context = request.Features.GetContext();
                return Task.FromResult(new IncomingResponse(request, InvalidConnection.IceRpc));
            }),
        });

        // Act
        await sut.InvokeAsync(
            "",
            SliceEncoding.Slice2,
            payload: null,
            payloadStream: null,
            defaultActivator: null,
            invocation);

        // Assert
        Assert.That(context, Is.EqualTo(invocation.Features.GetContext()));
    }

    

    /// <summary>Verifies that setting an invocation deadline requires providing a cancelable cancellation token.
    /// </summary>
    [Test]
    public void Setting_the_invocation_deadline_requires_a_cancelable_cancellation_token()
    {
        // Arrange
        var sut = new ServicePrx(new Proxy(Protocol.IceRpc));

        // Act/Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.InvokeAsync(
                "",
                SliceEncoding.Slice2,
                payload: null,
                payloadStream: null,
                defaultActivator: null,
                new Invocation { Deadline = DateTime.Now + TimeSpan.FromSeconds(60) },
                cancel: CancellationToken.None));
    }

    /// <summary>Verifies that the encoded deadline has the expected value when <see cref="Invocation.Deadline"/> is
    /// set.</summary>
    [Test]
    public async Task Invocation_deadline_sets_the_deadline_field()
    {
        // Arrange
        DateTime deadline = DateTime.MaxValue;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            deadline = DecodeDeadlineField(request.Fields);
            return Task.FromResult(new IncomingResponse(request, InvalidConnection.IceRpc));
        });

        using var cancellationSource = new CancellationTokenSource();
        var sut = new ServicePrx(new Proxy(Protocol.IceRpc) { Invoker = invoker });
        DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
        var invocation = new Invocation() { Deadline = expectedDeadline };

        // Act
        await sut.InvokeAsync(
            "",
            SliceEncoding.Slice2,
            payload: null,
            payloadStream: null,
            defaultActivator: null,
            invocation,
            cancel: cancellationSource.Token);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThanOrEqualTo(1));
    }

    private static DateTime DecodeDeadlineField(IDictionary<RequestFieldKey, OutgoingFieldValue> fields)
    {
        if (fields.TryGetValue(RequestFieldKey.Deadline, out var deadlineField))
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            deadlineField.Encode(ref encoder);
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            decoder.SkipSize();
            long value = decoder.DecodeVarInt62();
            return DateTime.UnixEpoch + TimeSpan.FromMilliseconds(value);
        }
        else
        {
            return DateTime.MaxValue;
        }
    }
}
