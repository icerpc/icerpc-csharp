// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Tests;

public sealed class TelemetryInterceptorTests
{
    /// <summary>Verifies that the invocation activity is created using the activity source when one is configured
    /// in the <see cref="Configure.TelemetryOptions"/>.</summary>
    [Test]
    public async Task Invocation_activity_created_from_activity_source()
    {
        // Arrange
        Activity? invocationActivity = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            invocationActivity = Activity.Current;
            return Task.FromResult(new IncomingResponse(request));
        });

        // Add a mock activity listener that allows the activity source to create the invocation activity.
        var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mookActivityListner = CreateMockActivityListener(activitySource);

        var sut = new TelemetryInterceptor(invoker, new Configure.TelemetryOptions()
        {
            ActivitySource = activitySource
        });

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/path" })
        {
            Operation = "Op"
        };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(invocationActivity, Is.Not.Null);
        Assert.That(invocationActivity.Kind, Is.EqualTo(ActivityKind.Client));
        Assert.That(invocationActivity.OperationName, Is.EqualTo($"{request.Proxy.Path}/{request.Operation}"));
        Assert.That(invocationActivity.Tags, Is.Not.Null);
        var tags = invocationActivity.Tags.ToDictionary(entry => entry.Key, entry => entry.Value);
        Assert.That(tags.ContainsKey("rpc.system"), Is.True);
        Assert.That(tags["rpc.system"], Is.EqualTo("icerpc"));
        Assert.That(tags.ContainsKey("rpc.service"), Is.True);
        Assert.That(tags["rpc.service"], Is.EqualTo(request.Proxy.Path));
        Assert.That(tags.ContainsKey("rpc.method"), Is.True);
        Assert.That(tags["rpc.method"], Is.EqualTo(request.Operation));
        Assert.That(request.Fields.ContainsKey(RequestFieldKey.TraceContext), Is.True);
    }

    /// <summary>Verifies that the invocation activity is created as a child of the current activity, if the current
    /// activity is not null.</summary>
    [Test]
    public async Task Invocation_activity_created_if_current_activity_is_not_null()
    {
        // Arrange
        Activity? invocationActivity = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            invocationActivity = Activity.Current;
            return Task.FromResult(new IncomingResponse(request));
        });

        var sut = new TelemetryInterceptor(invoker, new Configure.TelemetryOptions());
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/path" })
        {
            Operation = "Op"
        };

        // Start an activity to make it the current activity.
        using var testActivity = new Activity("TestActivity");
        testActivity.Start();

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(invocationActivity, Is.Not.Null);
        Assert.That(invocationActivity.ParentId, Is.EqualTo(testActivity.Id));
        Assert.That(invocationActivity.OperationName, Is.EqualTo($"{request.Proxy.Path}/{request.Operation}"));
        Assert.That(invocationActivity.Tags, Is.Not.Null);
        var tags = invocationActivity.Tags.ToDictionary(entry => entry.Key, entry => entry.Value);
        Assert.That(tags.ContainsKey("rpc.system"), Is.True);
        Assert.That(tags["rpc.system"], Is.EqualTo("icerpc"));
        Assert.That(tags.ContainsKey("rpc.service"), Is.True);
        Assert.That(tags["rpc.service"], Is.EqualTo(request.Proxy.Path));
        Assert.That(tags.ContainsKey("rpc.method"), Is.True);
        Assert.That(tags["rpc.method"], Is.EqualTo(request.Operation));
        Assert.That(request.Fields.ContainsKey(RequestFieldKey.TraceContext), Is.True);
    }

    /// <summary>Verifies that the invocation activity is created if the logger is enabled. This way the activity
    /// context can enrich the logger scopes.</summary>
    [Test]
    public async Task Invocation_activity_created_if_logger_is_enabled()
    {
        // Arrange
        Activity? invocationActivity = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            invocationActivity = Activity.Current;
            return Task.FromResult(new IncomingResponse(request));
        });

        // A mock logger factory to trigger the creation of the invocation activity
        var loggerFactory = new MockLoggerFactory();
        var sut = new TelemetryInterceptor(invoker, new Configure.TelemetryOptions()
        {
            LoggerFactory = loggerFactory
        });

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/path" })
        {
            Operation = "Op"
        };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(invocationActivity, Is.Not.Null);
        Assert.That(invocationActivity.OperationName, Is.EqualTo($"{request.Proxy.Path}/{request.Operation}"));
        Assert.That(invocationActivity.Tags, Is.Not.Null);
        var tags = invocationActivity.Tags.ToDictionary(entry => entry.Key, entry => entry.Value);
        Assert.That(tags.ContainsKey("rpc.system"), Is.True);
        Assert.That(tags["rpc.system"], Is.EqualTo("icerpc"));
        Assert.That(tags.ContainsKey("rpc.service"), Is.True);
        Assert.That(tags["rpc.service"], Is.EqualTo(request.Proxy.Path));
        Assert.That(tags.ContainsKey("rpc.method"), Is.True);
        Assert.That(tags["rpc.method"], Is.EqualTo(request.Operation));
        Assert.That(request.Fields.ContainsKey(RequestFieldKey.TraceContext), Is.True);
    }

    /// <summary>Verifies that the invocation activity context is encoded as a field with the
    /// <see cref="RequestFieldKey.TraceContext"/> key.</summary>
    [Test]
    public async Task Invocation_activity_encodes_trace_context_field()
    {
        // Arrange

        Activity? invocationActivity = null;
        Activity? decodedActivity = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (Activity.Current is Activity activity)
            {
                invocationActivity = activity;
                invocationActivity.AddBaggage("foo", "bar");
                decodedActivity = DecodeTraceContextField(request.Fields, "/op");
            }
            return Task.FromResult(new IncomingResponse(request));
        });

        var sut = new TelemetryInterceptor(invoker, new Configure.TelemetryOptions());
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/" })
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
        Assert.That(decodedActivity.ParentId, Is.EqualTo(invocationActivity.Id));
        Assert.That(decodedActivity.ParentSpanId, Is.EqualTo(invocationActivity.SpanId));
        Assert.That(decodedActivity.Baggage, Is.Not.Null);
        Assert.That(decodedActivity.ActivityTraceFlags, Is.EqualTo(invocationActivity.ActivityTraceFlags));
        var baggage = decodedActivity.Baggage.ToDictionary(x => x.Key, x => x.Value);
        Assert.That(baggage.ContainsKey("foo"), Is.True);
        Assert.That(baggage["foo"], Is.EqualTo("bar"));
    }

    /// <summary>Verifies that the trace context field is not added to the request fields when
    /// the invocation activity is null.</summary>
    [Test]
    public async Task Trace_context_field_not_encoded_if_activity_is_null()
    {
        // Arrange
        Activity? invocationActivity = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            invocationActivity = Activity.Current;
            return Task.FromResult(new IncomingResponse(request));
        });

        var sut = new TelemetryInterceptor(invoker, new Configure.TelemetryOptions());
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/" })
        {
            Operation = "op"
        };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(invocationActivity, Is.Null);
        Assert.That(request.Fields.ContainsKey(RequestFieldKey.TraceContext), Is.False);
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

    private static Activity? DecodeTraceContextField(
        IDictionary<RequestFieldKey, OutgoingFieldValue> fields,
        string operationName)
    {
        if (fields.TryGetValue(RequestFieldKey.TraceContext, out var traceContextField))
        {
            var buffer = new byte[1024];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, Encoding.Slice20);
            traceContextField.Encode(ref encoder);
            var decoder = new SliceDecoder(buffer, Encoding.Slice20);
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
