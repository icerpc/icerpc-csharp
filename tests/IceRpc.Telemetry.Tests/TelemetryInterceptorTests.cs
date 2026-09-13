// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Diagnostics;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;

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

    /// <summary>Verifies that an activity carrying more baggage entries than the maximum allowed is clipped
    /// during encoding, so a forwarding chain cannot amplify entry count across hops.</summary>
    [Test]
    public void Outgoing_baggage_is_clipped_to_maximum()
    {
        // Arrange
        using var activity = new Activity("/hello/Op");
        for (int i = 0; i < TelemetryInterceptor.MaxBaggageEntries + 10; i++)
        {
            activity.AddBaggage($"key{i}", $"value{i}");
        }
        activity.Start();

        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer);

        // Act
        TelemetryInterceptor.WriteActivityContext(ref encoder, activity);
        pipe.Writer.Complete();

        // Assert
        pipe.Reader.TryRead(out ReadResult readResult);
        using var decodedActivity = new Activity("/op");
        TelemetryMiddleware.RestoreActivityContext(readResult.Buffer, decodedActivity);
        Assert.That(decodedActivity.Baggage.Count(), Is.EqualTo(TelemetryInterceptor.MaxBaggageEntries));

        pipe.Reader.Complete();
    }

    /// <summary>Verifies that the interceptor forces W3C activity ID format even when the process-wide default is
    /// <c>Hierarchical</c>, and that the trace context field can be encoded and decoded successfully.</summary>
    /// <remarks>Marked <c>NonParallelizable</c> because it mutates <see cref="Activity.DefaultIdFormat" />, a
    /// process-wide setting; running it concurrently with other tests that create activities would make their
    /// observed ID format non-deterministic.</remarks>
    [Test]
    [NonParallelizable]
    public async Task Invocation_uses_w3c_format_regardless_of_process_default()
    {
        // Arrange
        ActivityIdFormat previousDefault = Activity.DefaultIdFormat;
        Activity.DefaultIdFormat = ActivityIdFormat.Hierarchical;
        try
        {
            Activity? invocationActivity = null;
            Activity? decodedActivity = null;
            var invoker = new InlineInvoker((request, cancellationToken) =>
            {
                invocationActivity = Activity.Current;
                decodedActivity = DecodeTraceContextField(request.Fields, request.Operation);
                return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
            });

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
            Assert.That(invocationActivity!.IdFormat, Is.EqualTo(ActivityIdFormat.W3C));
            Assert.That(decodedActivity, Is.Not.Null);
            Assert.That(decodedActivity!.ParentId, Is.EqualTo(invocationActivity.Id));
        }
        finally
        {
            Activity.DefaultIdFormat = previousDefault;
        }
    }

    /// <summary>Verifies that a response with a status code other than <see cref="StatusCode.Ok" /> marks the
    /// invocation activity as failed before it stops, and that the response is returned unchanged.</summary>
    [TestCase(StatusCode.NotFound, "NotFound")]
    [TestCase((StatusCode)42, "42")]
    public async Task Invocation_activity_records_failure_response(StatusCode statusCode, string expectedStatusCode)
    {
        // Arrange
        IncomingResponse? response = null;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            response = new IncomingResponse(request, FakeConnectionContext.Instance, statusCode, "error message");
            return Task.FromResult(response);
        });

        ActivityOutcome? outcome = null;
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(
            activitySource,
            activity => outcome = ActivityOutcome.From(activity));

        var sut = new TelemetryInterceptor(invoker, activitySource);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/path" })
        {
            Operation = "Op"
        };

        // Act
        IncomingResponse returnedResponse = await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(returnedResponse, Is.SameAs(response));
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Status, Is.EqualTo(ActivityStatusCode.Error));
        Assert.That(outcome.StatusDescription, Is.EqualTo("error message"));
        Assert.That(outcome.Tags, Does.ContainKey("rpc.status_code").WithValue(expectedStatusCode));
        Assert.That(outcome.Tags, Does.ContainKey("error.type").WithValue(expectedStatusCode));
    }

    /// <summary>Verifies that an exception thrown by the invocation marks the invocation activity as failed before it
    /// stops, and that the exception propagates unchanged.</summary>
    [TestCaseSource(nameof(InvocationExceptions))]
    public void Invocation_activity_records_exception(Exception exception)
    {
        // Arrange
        var invoker = new InlineInvoker(async (request, cancellationToken) =>
        {
            await Task.Yield();
            throw exception;
        });

        ActivityOutcome? outcome = null;
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(
            activitySource,
            activity => outcome = ActivityOutcome.From(activity));

        var sut = new TelemetryInterceptor(invoker, activitySource);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/path" })
        {
            Operation = "Op"
        };

        // Act
        Exception? thrownException = Assert.CatchAsync(async () => await sut.InvokeAsync(request, default));

        // Assert
        Assert.That(thrownException, Is.SameAs(exception));
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Status, Is.EqualTo(ActivityStatusCode.Error));
        Assert.That(outcome.StatusDescription, Is.EqualTo(exception.Message));
        Assert.That(outcome.Tags, Does.ContainKey("error.type").WithValue(exception.GetType().FullName));
        Assert.That(outcome.Tags, Does.Not.ContainKey("rpc.status_code"));
    }

    /// <summary>Verifies that a response with the <see cref="StatusCode.Ok" /> status code leaves the invocation
    /// activity status unset and records the status code.</summary>
    [Test]
    public async Task Invocation_activity_records_ok_response()
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));

        ActivityOutcome? outcome = null;
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(
            activitySource,
            activity => outcome = ActivityOutcome.From(activity));

        var sut = new TelemetryInterceptor(invoker, activitySource);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/path" })
        {
            Operation = "Op"
        };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Status, Is.EqualTo(ActivityStatusCode.Unset));
        Assert.That(outcome.StatusDescription, Is.Null);
        Assert.That(outcome.Tags, Does.ContainKey("rpc.status_code").WithValue("Ok"));
        Assert.That(outcome.Tags, Does.Not.ContainKey("error.type"));
    }

    private static IEnumerable<Exception> InvocationExceptions
    {
        get
        {
            yield return new InvalidOperationException("invocation failed");
            yield return new OperationCanceledException("invocation canceled");
        }
    }

    private static ActivityListener CreateMockActivityListener(
        ActivitySource activitySource,
        Action<Activity>? onActivityStopped = null)
    {
        var mockActivityListener = new ActivityListener();
        mockActivityListener.ActivityStarted = activity => { };
        mockActivityListener.ActivityStopped = activity => onActivityStopped?.Invoke(activity);
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
            var pipe = new Pipe();
            traceContextField.WriteAction!(pipe.Writer);
            pipe.Writer.Complete();

            pipe.Reader.TryRead(out var readResult);

            var activity = new Activity(operationName);
            TelemetryMiddleware.RestoreActivityContext(readResult.Buffer, activity);
            return activity;
        }
        else
        {
            return null;
        }
    }

    /// <summary>The status and tags of an activity, captured when the activity stops.</summary>
    private sealed record class ActivityOutcome(
        ActivityStatusCode Status,
        string? StatusDescription,
        IReadOnlyDictionary<string, string?> Tags)
    {
        internal static ActivityOutcome From(Activity activity) =>
            new(
                activity.Status,
                activity.StatusDescription,
                activity.Tags.ToDictionary(entry => entry.Key, entry => entry.Value));
    }
}
