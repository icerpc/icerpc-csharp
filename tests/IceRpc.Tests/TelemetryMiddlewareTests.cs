// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Tests;

public sealed class TelemetryMiddlewareTests
{
    /// <summary>Verifies that the dispatch activity is created using the activity source when one is configured
    /// in the <see cref="Configure.TelemetryOptions"/>.</summary>
    [Test]
    public async Task Dispatch_activity_created_from_activity_source()
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        // Add a mock activity listener that allows the activity source to create the dispatch activity.
        var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);
        var sut = new TelemetryMiddleware(dispatcher, new Configure.TelemetryOptions()
        {
            ActivitySource = activitySource
        });

        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Operation = "Op",
            Path = "/"
        };

        // Act
        await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(dispatchActivity, Is.Not.Null);
        Assert.That(dispatchActivity.Kind, Is.EqualTo(ActivityKind.Server));
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

    /// <summary>Verifies that the dispatch activity is created as a child of the current activity, if the current
    /// activity is not null.</summary>
    [Test]
    public async Task Dispatch_activity_created_if_current_activity_is_not_null()
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        var sut = new TelemetryMiddleware(dispatcher, new Configure.TelemetryOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Operation = "Op",
            Path = "/"
        };

        // Start an activity to make it the current activity.
        using var testActivity = new Activity("TestActivity");
        testActivity.Start();

        // Act
        await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(dispatchActivity, Is.Not.Null);
        Assert.That(dispatchActivity.ParentId, Is.EqualTo(testActivity.Id));
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

    /// <summary>Verifies that the dispatch activity is created if the logger is enabled. This way the activity context
    /// can enrich the logger scopes.</summary>
    [Test]
    public async Task Dispatch_activity_created_if_logger_is_enabled()
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        // A mock logger factory to trigger the creation of the dispatch activity
        var loggerFactory = new MockLoggerFactory();
        var sut = new TelemetryMiddleware(dispatcher, new Configure.TelemetryOptions()
        {
            LoggerFactory = loggerFactory
        });

        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Operation = "Op",
            Path = "/"
        };

        // Act
        await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(dispatchActivity, Is.Not.Null);
        Assert.That(dispatchActivity.OperationName, Is.EqualTo($"{request.Path}/{request.Operation}"));
        Assert.That(dispatchActivity.Tags, Is.Not.Null);
        var tags = dispatchActivity.Tags.ToDictionary(x => x.Key, x => x.Value);
        Assert.That(tags.ContainsKey("rpc.system"), Is.True);
        Assert.That(tags["rpc.system"], Is.EqualTo("icerpc"));
        Assert.That(tags.ContainsKey("rpc.service"), Is.True);
        Assert.That(tags["rpc.service"], Is.EqualTo(request.Path));
        Assert.That(tags.ContainsKey("rpc.method"), Is.True);
        Assert.That(tags["rpc.method"], Is.EqualTo(request.Operation));
    }

    /// <summary>Verifies that the dispatch activity context is restored from the
    /// <see cref="RequestFieldKey.TraceContext"/> field.</summary>
    [Test]
    public async Task Dispatch_activity_decodes_trace_context_field()
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        string? encodedActivityId;
        ActivitySpanId? parentSpandId;
        ReadOnlySequence<byte>? encodedTraceContext = EncodeTraceContext();

        ReadOnlySequence<byte> EncodeTraceContext()
        {
            // Encode the parent activity context here, in a separate scope, we don't want this activity to be running
            // when we call dispatch on the "sut" as the telemetry middleware interacts with Activity.Current.
            using var encodedActivity = new Activity("/hello/Op");
            encodedActivity.AddBaggage("foo", "bar");
            encodedActivity.Start();
            encodedActivityId = encodedActivity.Id;
            parentSpandId = encodedActivity.SpanId;

            var buffer = new byte[1024];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, Encoding.Slice20);

            TelemetryInterceptor.WriteActivityContext(ref encoder, encodedActivity);

            return new ReadOnlySequence<byte>(buffer, 0, encoder.EncodedByteCount);
        }

        // Add a mock activity listener that allows the activity source to create the dispatch activity.
        var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);
        var sut = new TelemetryMiddleware(dispatcher, new Configure.TelemetryOptions()
        {
            ActivitySource = activitySource
        });

        // Create an incoming request that carries the encoded trace context
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Operation = "Op",
            Path = "/",
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            { 
                [RequestFieldKey.TraceContext] = encodedTraceContext.Value
            }
        };

        // Act
        await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(dispatchActivity, Is.Not.Null);
        // The dispatch activity parent matches the activity context encoded in the TraceContext field
        Assert.That(dispatchActivity.ParentId, Is.EqualTo(encodedActivityId));
        Assert.That(dispatchActivity.ParentSpanId, Is.EqualTo(parentSpandId));
        Assert.That(dispatchActivity.ActivityTraceFlags, Is.EqualTo(ActivityTraceFlags.None));
        Assert.That(dispatchActivity.Baggage, Is.Not.Null);
        var baggage = dispatchActivity.Baggage.ToDictionary(x => x.Key, x => x.Value);
        Assert.That(baggage.ContainsKey("foo"), Is.True);
        Assert.That(baggage["foo"], Is.EqualTo("bar"));
    }

    /// <summary>Verifies that the dispatch activity context is restored from the
    /// <see cref="RequestFieldKey.TraceContext"/> field.</summary>
    [Test]
    public void Decoding_empty_trace_context_field_fails()
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        // Add a mock activity listener that allows the activity source to create the dispatch activity.
        var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(activitySource);
        var sut = new TelemetryMiddleware(dispatcher, new Configure.TelemetryOptions()
        {
            ActivitySource = activitySource
        });

        // Create an incoming request that carries an empty trace context field
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Operation = "Op",
            Path = "/",
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.TraceContext] = ReadOnlySequence<byte>.Empty
            }
        };

        // Act/Assert
        Assert.That(async () => await sut.DispatchAsync(request, default), Throws.InstanceOf<InvalidDataException>());
    }

    private static ActivityListener CreateMockActivityListener(ActivitySource activitySource)
    {
        var mookActivityListner = new ActivityListener();
        mookActivityListner.ActivityStarted = activity => { };
        mookActivityListner.ActivityStopped = activity => { };
        mookActivityListner.ShouldListenTo = source => ReferenceEquals(source, activitySource);
        mookActivityListner.Sample =
            (ref ActivityCreationOptions<ActivityContext> activityOptions) => ActivitySamplingResult.AllData;
        mookActivityListner.SampleUsingParentId =
            (ref ActivityCreationOptions<string> activityOptions) => ActivitySamplingResult.AllData;
        ActivitySource.AddActivityListener(mookActivityListner);
        return mookActivityListner;
    }

    class MockLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?,
            string> formatter)
        {
        }
    }

    class MockLoggerFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => new MockLogger();
        public void Dispose() { }
    }
}
