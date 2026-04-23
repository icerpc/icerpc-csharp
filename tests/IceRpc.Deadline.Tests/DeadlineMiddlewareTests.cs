// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;

namespace IceRpc.Deadline.Tests;

[NonParallelizable]
public sealed class DeadlineMiddlewareTests
{
    /// <summary>Verifies that the dispatch throws DispatchException when the deadline expires.</summary>
    [Test]
    public async Task Dispatch_fails_after_the_deadline_expires()
    {
        // Arrange
        CancellationToken? token = null;
        bool hasDeadline = false;

        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            hasDeadline = request.Fields.ContainsKey(RequestFieldKey.Deadline);
            token = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new OutgoingResponse(request);
        });

        var sut = new DeadlineMiddleware(dispatcher);

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
        PipeReader pipeReader = WriteDeadline(deadline);
        pipeReader.TryRead(out var readResult);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Deadline] = readResult.Buffer
            }
        };

        // Act
        OutgoingResponse response = await sut.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(StatusCode.DeadlineExceeded));
        Assert.That(hasDeadline, Is.True);
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Value.CanBeCanceled, Is.True);
        Assert.That(token.Value.IsCancellationRequested, Is.True);

        // Cleanup
        pipeReader.Complete();
    }

    /// <summary>Verifies that a request with an explicitly encoded DateTime.MinValue deadline returns
    /// DeadlineExceeded and does not fall through to the next dispatcher. This is the decoded form of a peer-encoded
    /// ticks=0 deadline, which must be treated as an expired deadline rather than as an absent field.</summary>
    [Test]
    public async Task Dispatch_fails_when_deadline_is_min_value()
    {
        // Arrange
        bool nextCalled = false;
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            nextCalled = true;
            return new(new OutgoingResponse(request));
        });

        var sut = new DeadlineMiddleware(dispatcher);

        // Encode an explicit ticks=0 deadline: the Utc kind avoids ToUniversalTime shifting it away from 0 in
        // timezones other than UTC. Decoded server-side as DateTime.MinValue, which collides with the "field
        // absent" sentinel the middleware used to rely on.
        PipeReader pipeReader = WriteDeadline(new DateTime(0, DateTimeKind.Utc));
        pipeReader.TryRead(out var readResult);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Deadline] = readResult.Buffer
            }
        };

        // Act
        OutgoingResponse response = await sut.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(StatusCode.DeadlineExceeded));
        Assert.That(nextCalled, Is.False);

        // Cleanup
        pipeReader.Complete();
    }

    /// <summary>Verifies the middleware clamps an extreme-future peer-encoded deadline instead of letting
    /// CancelAfter throw ArgumentOutOfRangeException.</summary>
    [Test]
    public async Task Dispatch_with_extreme_future_deadline_does_not_throw()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
            new(new OutgoingResponse(request)));

        var sut = new DeadlineMiddleware(dispatcher);

        PipeReader pipeReader = WriteDeadline(
            DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
        pipeReader.TryRead(out var readResult);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Deadline] = readResult.Buffer
            }
        };

        // Act/Assert
        Assert.That(async () => await sut.DispatchAsync(request, CancellationToken.None), Throws.Nothing);

        // Cleanup
        pipeReader.Complete();
    }

    /// <summary>Verifies that the deadline decoded by the middleware has the expected value.</summary>
    [Test]
    public async Task Deadline_decoded_by_middleware_has_expected_value()
    {
        // Arrange
        DateTime deadline = DateTime.MaxValue;
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            deadline = request.Features.Get<IDeadlineFeature>()?.Value ?? deadline;
            return new(new OutgoingResponse(request));
        });

        var sut = new DeadlineMiddleware(dispatcher);

        DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        PipeReader pipeReader = WriteDeadline(expectedDeadline);
        pipeReader.TryRead(out var readResult);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Deadline] = readResult.Buffer
            }
        };

        // Act
        _ = await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThanOrEqualTo(1));

        // Cleanup
        pipeReader.Complete();
    }

    private static PipeReader WriteDeadline(DateTime deadline)
    {
        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer);
        encoder.EncodeTimeStamp(deadline);
        pipe.Writer.Complete();

        return pipe.Reader;
    }
}
