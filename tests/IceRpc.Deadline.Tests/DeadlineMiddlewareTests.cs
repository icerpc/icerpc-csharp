// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Deadline.Tests;

public sealed class DeadlineMiddlewareTests
{
    /// <summary>Verifies that the dispatch throws DispatchException when the deadline expires.</summary>
    [Test]
    [NonParallelizable]
    public async Task Dispatch_fails_after_the_deadline_expires()
    {
        // Arrange
        CancellationToken? cancellationToken = null;
        bool hasDeadline = false;

        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            hasDeadline = request.Fields.ContainsKey(RequestFieldKey.Deadline);
            cancellationToken = cancel;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancel);
            return new OutgoingResponse(request);
        });

        var sut = new DeadlineMiddleware(dispatcher);

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(10);
        PipeReader pipeReader = WriteDeadline(deadline);
        pipeReader.TryRead(out var readResult);

        var request = new IncomingRequest(FakeConnectionContext.IceRpc)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Deadline] = readResult.Buffer
            }
        };

        // Act
        DispatchException exception =
            Assert.ThrowsAsync<DispatchException>(async () => await sut.DispatchAsync(request, CancellationToken.None));

        // Assert
        Assert.That(exception.ErrorCode, Is.EqualTo(DispatchErrorCode.DeadlineExpired));
        Assert.That(hasDeadline, Is.True);
        Assert.That(cancellationToken, Is.Not.Null);
        Assert.That(cancellationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancellationToken.Value.IsCancellationRequested, Is.True);

        // Cleanup
        await pipeReader.CompleteAsync();
    }

    /// <summary>Verifies that the deadline decoded by the middleware has the expected value.</summary>
    [Test]
    public async Task Deadline_decoded_by_middleware_has_expected_value()
    {
        // Arrange
        DateTime deadline = DateTime.MaxValue;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            deadline = request.Features.Get<IDeadlineFeature>()?.Value ?? deadline;
            return new(new OutgoingResponse(request));
        });

        var sut = new DeadlineMiddleware(dispatcher);

        DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        PipeReader pipeReader = WriteDeadline(expectedDeadline);
        pipeReader.TryRead(out var readResult);

        var request = new IncomingRequest(FakeConnectionContext.IceRpc)
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
        await pipeReader.CompleteAsync();
    }

    private static PipeReader WriteDeadline(DateTime deadline)
    {
        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
        long deadlineValue = (long)(deadline - DateTime.UnixEpoch).TotalMilliseconds;
        encoder.EncodeVarInt62(deadlineValue);
        pipe.Writer.Complete();

        return pipe.Reader;
    }
}
