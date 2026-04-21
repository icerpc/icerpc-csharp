// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;

namespace IceRpc.Telemetry.Tests;

public sealed class TelemetryMiddlewareTests
{
    /// <summary>Verifies that the dispatch activity is created using the activity source used to create the
    /// <see cref="TelemetryMiddleware" />.</summary>
    [Test]
    public async Task Dispatch_activity_created_from_activity_source()
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        // Add a mock activity listener that allows the activity source to create the dispatch activity.
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Operation = "Op",
            Path = "/"
        };

        // Act
        await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(dispatchActivity, Is.Not.Null);
        Assert.That(dispatchActivity!.Kind, Is.EqualTo(ActivityKind.Server));
        Assert.That(dispatchActivity.OperationName, Is.EqualTo($"{request.Path}/{request.Operation}"));
        Assert.That(dispatchActivity.Tags, Is.Not.Null);
        var tags = dispatchActivity.Tags.ToDictionary(entry => entry.Key, entry => entry.Value);
        Assert.That(tags.ContainsKey("rpc.system"), Is.True);
        Assert.That(tags["rpc.system"], Is.EqualTo("icerpc"));
        Assert.That(tags.ContainsKey("rpc.service"), Is.True);
        Assert.That(tags["rpc.service"], Is.EqualTo(request.Path));
        Assert.That(tags.ContainsKey("rpc.method"), Is.True);
        Assert.That(tags["rpc.method"], Is.EqualTo(request.Operation));
    }

    /// <summary>Verifies that the dispatch activity context is restored from the
    /// <see cref="RequestFieldKey.TraceContext" /> field.</summary>
    [Test]
    public async Task Dispatch_activity_decodes_trace_context_field()
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        string? encodedActivityId;
        ActivitySpanId? parentSpanId;
        PipeReader encodedTraceContext = EncodeTraceContext();

        PipeReader EncodeTraceContext()
        {
            // Encode the parent activity context here, in a separate scope, we don't want this activity to be running
            // when we call dispatch on the "sut" as the telemetry middleware interacts with Activity.Current.
            using var encodedActivity = new Activity("/hello/Op");
            encodedActivity.AddBaggage("foo", "bar");
            encodedActivity.Start();
            encodedActivityId = encodedActivity.Id;
            parentSpanId = encodedActivity.SpanId;

            var pipe = new Pipe();
            var encoder = new SliceEncoder(pipe.Writer);
            TelemetryInterceptor.WriteActivityContext(ref encoder, encodedActivity);
            pipe.Writer.Complete();

            return pipe.Reader;
        }

        // Add a mock activity listener that allows the activity source to create the dispatch activity.
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        encodedTraceContext.TryRead(out ReadResult readResult);

        // Create an incoming request that carries the encoded trace context
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.TraceContext] = readResult.Buffer
            },
            Operation = "Op",
            Path = "/"
        };

        // Act
        await sut.DispatchAsync(request, default);

        // Cleanup
        encodedTraceContext.Complete();

        // Assert
        Assert.That(dispatchActivity, Is.Not.Null);
        // The dispatch activity parent matches the activity context encoded in the TraceContext field
        Assert.That(dispatchActivity!.ParentId, Is.EqualTo(encodedActivityId));
        Assert.That(dispatchActivity.ParentSpanId, Is.EqualTo(parentSpanId));
        Assert.That(dispatchActivity.ActivityTraceFlags, Is.EqualTo(ActivityTraceFlags.None));
        Assert.That(dispatchActivity.Baggage, Is.Not.Null);
        var baggage = dispatchActivity.Baggage.ToDictionary(x => x.Key, x => x.Value);
        Assert.That(baggage.ContainsKey("foo"), Is.True);
        Assert.That(baggage["foo"], Is.EqualTo("bar"));
    }

    /// <summary>Verifies that the dispatch activity context is restored from the
    /// <see cref="RequestFieldKey.TraceContext" /> field.</summary>
    [Test]
    public void Decoding_empty_trace_context_field_fails()
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        // Add a mock activity listener that allows the activity source to create the dispatch activity.
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        // Create an incoming request that carries an empty trace context field
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.TraceContext] = ReadOnlySequence<byte>.Empty
            },
            Operation = "Op",
            Path = "/",
        };

        // Act/Assert
        Assert.That(async () => await sut.DispatchAsync(request, default), Throws.InstanceOf<InvalidDataException>());
    }

    /// <summary>Verifies that a trace context carrying baggage at the maximum allowed entry count
    /// (180, per the W3C Baggage spec soft limit) is decoded successfully.</summary>
    [Test]
    public async Task Dispatch_activity_decodes_trace_context_with_maximum_baggage()
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        PipeReader encodedTraceContext = EncodeTraceContextWithBaggage(entryCount: 180);

        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        encodedTraceContext.TryRead(out ReadResult readResult);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.TraceContext] = readResult.Buffer
            },
            Operation = "Op",
            Path = "/"
        };

        // Act
        await sut.DispatchAsync(request, default);

        // Cleanup
        encodedTraceContext.Complete();

        // Assert
        Assert.That(dispatchActivity, Is.Not.Null);
        Assert.That(dispatchActivity!.Baggage.Count(), Is.EqualTo(180));
    }

    /// <summary>Verifies that a trace context carrying more than the maximum allowed number of baggage
    /// entries is rejected with <see cref="InvalidDataException" />.</summary>
    [Test]
    public void Decoding_trace_context_with_excessive_baggage_fails()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
            new(new OutgoingResponse(request)));

        PipeReader encodedTraceContext = EncodeTraceContextWithRawBaggage(entryCount: 181);

        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        encodedTraceContext.TryRead(out ReadResult readResult);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.TraceContext] = readResult.Buffer
            },
            Operation = "Op",
            Path = "/"
        };

        // Act/Assert
        Assert.That(async () => await sut.DispatchAsync(request, default), Throws.InstanceOf<InvalidDataException>());

        // Cleanup
        encodedTraceContext.Complete();
    }

    private static PipeReader EncodeTraceContextWithBaggage(int entryCount)
    {
        // Encode the parent activity context in a separate scope so it doesn't leak into Activity.Current.
        using var encodedActivity = new Activity("/hello/Op");
        for (int i = 0; i < entryCount; i++)
        {
            encodedActivity.AddBaggage($"key{i}", $"value{i}");
        }
        encodedActivity.Start();

        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer);
        TelemetryInterceptor.WriteActivityContext(ref encoder, encodedActivity);
        pipe.Writer.Complete();

        return pipe.Reader;
    }

    // Mirrors TelemetryInterceptor.WriteActivityContext but writes the baggage sequence raw so tests can
    // simulate a peer that did not honor the 180-entry clip on its outgoing path.
    private static PipeReader EncodeTraceContextWithRawBaggage(int entryCount)
    {
        using var activity = new Activity("/hello/Op");
        activity.Start();

        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer);

        encoder.EncodeUInt8(0);
        using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(16);
        Span<byte> buffer = memoryOwner.Memory.Span[..16];
        activity.TraceId.CopyTo(buffer);
        encoder.WriteByteSpan(buffer);
        activity.SpanId.CopyTo(buffer[..8]);
        encoder.WriteByteSpan(buffer[..8]);
        encoder.EncodeUInt8((byte)activity.ActivityTraceFlags);
        encoder.EncodeString(activity.TraceStateString ?? "");

        encoder.EncodeSize(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            encoder.EncodeString($"key{i}");
            encoder.EncodeString($"value{i}");
        }

        pipe.Writer.Complete();
        return pipe.Reader;
    }

    private static ActivityListener CreateMockActivityListener(ActivitySource activitySource)
    {
        var mockActivityListener = new ActivityListener();
        mockActivityListener.ActivityStarted = activity => { };
        mockActivityListener.ActivityStopped = activity => { };
        mockActivityListener.ShouldListenTo = source => ReferenceEquals(source, activitySource);
        mockActivityListener.Sample =
            (ref ActivityCreationOptions<ActivityContext> activityOptions) => ActivitySamplingResult.AllData;
        mockActivityListener.SampleUsingParentId =
            (ref ActivityCreationOptions<string> activityOptions) => ActivitySamplingResult.AllData;
        ActivitySource.AddActivityListener(mockActivityListener);
        return mockActivityListener;
    }
}
