// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Telemetry.Tests;

public sealed class TelemetryInterceptorTests
{
    /// <summary>Verifies that the invocation activity is created using the activity source used to create the
    /// <see cref="TelemetryInterceptor" />.</summary>
    [Test]
    public async Task Invocation_activity_created_from_activity_source()
    {
        // Arrange
        Activity? invocationActivity = null;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            invocationActivity = Activity.Current;
            return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
        });

        // Add a mock activity listener that allows the activity source to create the invocation activity.
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);

        var sut = new TelemetryInterceptor(invoker, activitySource);

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/path" })
        {
            Operation = "Op"
        };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(invocationActivity, Is.Not.Null);
        Assert.That(invocationActivity!.Kind, Is.EqualTo(ActivityKind.Client));
        Assert.That(invocationActivity.OperationName, Is.EqualTo($"{request.ServiceAddress.Path}/{request.Operation}"));
        Assert.That(invocationActivity.Tags, Is.Not.Null);
        var tags = invocationActivity.Tags.ToDictionary(entry => entry.Key, entry => entry.Value);
        Assert.That(tags.ContainsKey("rpc.system"), Is.True);
        Assert.That(tags["rpc.system"], Is.EqualTo("icerpc"));
        Assert.That(tags.ContainsKey("rpc.service"), Is.True);
        Assert.That(tags["rpc.service"], Is.EqualTo(request.ServiceAddress.Path));
        Assert.That(tags.ContainsKey("rpc.method"), Is.True);
        Assert.That(tags["rpc.method"], Is.EqualTo(request.Operation));
        Assert.That(request.Fields.ContainsKey(RequestFieldKey.TraceContext), Is.True);
    }

    /// <summary>Verifies that the invocation activity context is encoded as a field with the
    /// <see cref="RequestFieldKey.TraceContext" /> key.</summary>
    [Test]
    public async Task Invocation_activity_encodes_trace_context_field()
    {
        // Arrange

        Activity? invocationActivity = null;
        Activity? decodedActivity = null;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (Activity.Current is Activity activity)
            {
                invocationActivity = activity;
                invocationActivity.AddBaggage("foo", "bar");
                decodedActivity = DecodeTraceContextField(request.Fields, "/op");
            }
            return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
        });

        // Add a mock activity listener that allows the activity source to create the invocation activity.
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);

        var sut = new TelemetryInterceptor(invoker, activitySource);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/" })
        {
            Operation = "op"
        };

        // Start an activity to make it the current activity.
        using var testActivity = new Activity("TestActivity");
        testActivity.Start();

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(invocationActivity, Is.Not.Null);
        Assert.That(decodedActivity, Is.Not.Null);
        // The decode activity parent is the invocation activity
        Assert.That(decodedActivity!.ParentId, Is.EqualTo(invocationActivity!.Id));
        Assert.That(decodedActivity.ParentSpanId, Is.EqualTo(invocationActivity.SpanId));
        Assert.That(decodedActivity.Baggage, Is.Not.Null);
        Assert.That(decodedActivity.ActivityTraceFlags, Is.EqualTo(invocationActivity.ActivityTraceFlags));
        var baggage = decodedActivity.Baggage.ToDictionary(x => x.Key, x => x.Value);
        Assert.That(baggage.ContainsKey("foo"), Is.True);
        Assert.That(baggage["foo"], Is.EqualTo("bar"));
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

    private static Activity? DecodeTraceContextField(
        IDictionary<RequestFieldKey, OutgoingFieldValue> fields,
        string operationName)
    {
        if (fields.TryGetValue(RequestFieldKey.TraceContext, out var traceContextField))
        {
            var buffer = new byte[1024];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            traceContextField.Encode(ref encoder);
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            decoder.SkipSize();

            var activity = new Activity(operationName);
            TelemetryMiddleware.RestoreActivityContext(
                new ReadOnlySequence<byte>(buffer, (int)decoder.Consumed, encoder.EncodedByteCount),
                activity);
            return activity;

        }
        else
        {
            return null;
        }
    }
}
