// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using ZeroC.Slice;
using ZeroC.Slice.WellKnownTypes;

namespace IceRpc.Deadline.Tests;

public sealed class DeadlineMiddlewareTests
{
    /// <summary>Verifies that the dispatch throws DispatchException when the deadline expires.</summary>
    [Test]
    [NonParallelizable]
    public void Dispatch_fails_after_the_deadline_expires()
    {
        // Arrange
        CancellationToken? token = null;
        bool hasDeadline = false;

        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            hasDeadline = request.Fields.ContainsKey(RequestFieldKey.Deadline);
            token = cancellationToken;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
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
        DispatchException? exception =
            Assert.ThrowsAsync<DispatchException>(async () => await sut.DispatchAsync(request, CancellationToken.None));

        // Assert
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.DeadlineExpired));
        Assert.That(hasDeadline, Is.True);
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Value.CanBeCanceled, Is.True);
        Assert.That(token.Value.IsCancellationRequested, Is.True);

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
        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
        encoder.EncodeTimeStamp(deadline);
        pipe.Writer.Complete();

        return pipe.Reader;
    }
}
