// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.Diagnostics;

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
        ReadOnlySequence<byte>? encodedTraceContext = EncodeTraceContext();

        ReadOnlySequence<byte> EncodeTraceContext()
        {
            // Encode the parent activity context here, in a separate scope, we don't want this activity to be running
            // when we call dispatch on the "sut" as the telemetry middleware interacts with Activity.Current.
            using var encodedActivity = new Activity("/hello/Op");
            encodedActivity.AddBaggage("foo", "bar");
            encodedActivity.Start();
            encodedActivityId = encodedActivity.Id;
            parentSpanId = encodedActivity.SpanId;

            var buffer = new byte[1024];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            TelemetryInterceptor.WriteActivityContext(ref encoder, encodedActivity);

            return new ReadOnlySequence<byte>(buffer, 0, encoder.EncodedByteCount);
        }

        // Add a mock activity listener that allows the activity source to create the dispatch activity.
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        // Create an incoming request that carries the encoded trace context
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.TraceContext] = encodedTraceContext.Value
            },
            Operation = "Op",
            Path = "/"
        };

        // Act
        await sut.DispatchAsync(request, default);

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
