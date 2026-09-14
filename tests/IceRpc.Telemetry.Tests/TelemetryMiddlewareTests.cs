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

    /// <summary>Verifies that the decoded baggage count equals <c>min(inputEntryCount,
    /// <see cref="TelemetryInterceptor.MaxBaggageEntries"/>)</c>: under-cap input round-trips fully, and
    /// over-cap input is silently clipped to the cap — consistent with OpenTelemetry's no-throw policy
    /// for observability operations.</summary>
    [TestCase(0)]
    [TestCase(TelemetryInterceptor.MaxBaggageEntries / 2)]
    [TestCase(TelemetryInterceptor.MaxBaggageEntries)]
    [TestCase(TelemetryInterceptor.MaxBaggageEntries + 1)]
    [TestCase(TelemetryInterceptor.MaxBaggageEntries * 2)]
    public async Task Dispatch_activity_clips_trace_context_baggage_to_maximum(int inputEntryCount)
    {
        // Arrange
        Activity? dispatchActivity = null;
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            dispatchActivity = Activity.Current;
            return new(new OutgoingResponse(request));
        });

        PipeReader encodedTraceContext = EncodeTraceContextWithRawBaggage(inputEntryCount);

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
        int expected = Math.Min(inputEntryCount, TelemetryInterceptor.MaxBaggageEntries);
        Assert.That(dispatchActivity!.Baggage.Count(), Is.EqualTo(expected));
    }

    /// <summary>Verifies that a response with a status code that reports a server failure marks the dispatch activity
    /// as failed before it stops, and that the response is returned unchanged. An undefined status code is recorded
    /// as is, but its error type is <c>_OTHER</c>.</summary>
    [TestCase(StatusCode.NotImplemented, "NotImplemented", "NotImplemented")]
    [TestCase(StatusCode.Unavailable, "Unavailable", "Unavailable")]
    [TestCase(StatusCode.InternalError, "InternalError", "InternalError")]
    [TestCase(StatusCode.DeadlineExceeded, "DeadlineExceeded", "DeadlineExceeded")]
    [TestCase(StatusCode.NotSupported, "NotSupported", "NotSupported")]
    [TestCase((StatusCode)42, "42", "_OTHER")]
    public async Task Dispatch_activity_records_server_error_response(
        StatusCode statusCode,
        string expectedStatusCode,
        string expectedErrorType)
    {
        // Arrange
        OutgoingResponse? response = null;
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            response = new OutgoingResponse(request, statusCode, "error message");
            return new(response);
        });

        ActivityOutcome? outcome = null;
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(
            activitySource,
            activity => outcome = ActivityOutcome.From(activity));
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Operation = "Op",
            Path = "/"
        };

        // Act
        OutgoingResponse returnedResponse = await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(returnedResponse, Is.SameAs(response));
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Status, Is.EqualTo(ActivityStatusCode.Error));
        Assert.That(outcome.StatusDescription, Is.EqualTo("error message"));
        Assert.That(outcome.Tags, Does.ContainKey("rpc.status_code").WithValue(expectedStatusCode));
        Assert.That(outcome.Tags, Does.ContainKey("error.type").WithValue(expectedErrorType));
    }

    /// <summary>Verifies that a response with a status code that reports a problem with the request records the
    /// status code but leaves the dispatch activity status unset.</summary>
    [TestCase(StatusCode.ApplicationError, "ApplicationError")]
    [TestCase(StatusCode.NotFound, "NotFound")]
    [TestCase(StatusCode.InvalidData, "InvalidData")]
    [TestCase(StatusCode.TruncatedPayload, "TruncatedPayload")]
    [TestCase(StatusCode.Unauthorized, "Unauthorized")]
    public async Task Dispatch_activity_records_request_error_response(StatusCode statusCode, string expectedStatusCode)
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
            new(new OutgoingResponse(request, statusCode, "error message")));

        ActivityOutcome? outcome = null;
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(
            activitySource,
            activity => outcome = ActivityOutcome.From(activity));
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Operation = "Op",
            Path = "/"
        };

        // Act
        await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Status, Is.EqualTo(ActivityStatusCode.Unset));
        Assert.That(outcome.StatusDescription, Is.Null);
        Assert.That(outcome.Tags, Does.ContainKey("rpc.status_code").WithValue(expectedStatusCode));
        Assert.That(outcome.Tags, Does.Not.ContainKey("error.type"));
    }

    /// <summary>Verifies that an exception thrown by the dispatch records the status code and the error message of the
    /// failure response the caller receives, marks the dispatch activity as failed before it stops when this status
    /// code reports a server failure, and that the exception propagates unchanged.</summary>
    [TestCaseSource(nameof(ServerErrorExceptions))]
    public void Dispatch_activity_records_server_error_exception(
        Exception exception,
        string expectedStatusCode,
        string expectedErrorType,
        string expectedErrorMessage)
    {
        // Arrange
        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            await Task.Yield();
            throw exception;
        });

        ActivityOutcome? outcome = null;
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(
            activitySource,
            activity => outcome = ActivityOutcome.From(activity));
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Operation = "Op",
            Path = "/"
        };

        // Act
        Exception? thrownException = Assert.CatchAsync(async () => await sut.DispatchAsync(request, default));

        // Assert
        Assert.That(thrownException, Is.SameAs(exception));
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Status, Is.EqualTo(ActivityStatusCode.Error));
        Assert.That(outcome.StatusDescription, Is.EqualTo(expectedErrorMessage));
        Assert.That(outcome.Tags, Does.ContainKey("rpc.status_code").WithValue(expectedStatusCode));
        Assert.That(outcome.Tags, Does.ContainKey("error.type").WithValue(expectedErrorType));
        Assert.That(outcome.Tags, Does.Not.ContainKey("icerpc.canceled"));
    }

    /// <summary>Verifies that an exception thrown by the dispatch that the caller receives as a status code that
    /// reports a problem with the request records this status code but leaves the dispatch activity status unset, like
    /// the equivalent response.</summary>
    [TestCaseSource(nameof(RequestErrorExceptions))]
    public void Dispatch_activity_records_request_error_exception(Exception exception, string expectedStatusCode)
    {
        // Arrange
        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            await Task.Yield();
            throw exception;
        });

        ActivityOutcome? outcome = null;
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(
            activitySource,
            activity => outcome = ActivityOutcome.From(activity));
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Operation = "Op",
            Path = "/"
        };

        // Act
        Exception? thrownException = Assert.CatchAsync(async () => await sut.DispatchAsync(request, default));

        // Assert
        Assert.That(thrownException, Is.SameAs(exception));
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Status, Is.EqualTo(ActivityStatusCode.Unset));
        Assert.That(outcome.StatusDescription, Is.Null);
        Assert.That(outcome.Tags, Does.ContainKey("rpc.status_code").WithValue(expectedStatusCode));
        Assert.That(outcome.Tags, Does.Not.ContainKey("error.type"));
    }

    /// <summary>Verifies that a cancellation by the token passed to the middleware is not recorded as a failure: the
    /// dispatch activity status stays unset, the <c>icerpc.canceled</c> tag is set, and no status code is recorded
    /// since the caller receives no response.</summary>
    [Test]
    public void Dispatch_activity_records_cancellation()
    {
        // Arrange
        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return new OutgoingResponse(request);
        });

        ActivityOutcome? outcome = null;
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(
            activitySource,
            activity => outcome = ActivityOutcome.From(activity));
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Operation = "Op",
            Path = "/"
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act/Assert
        Assert.That(
            async () => await sut.DispatchAsync(request, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Status, Is.EqualTo(ActivityStatusCode.Unset));
        Assert.That(outcome.StatusDescription, Is.Null);
        Assert.That(outcome.Tags, Does.ContainKey("icerpc.canceled").WithValue(true));
        Assert.That(outcome.Tags, Does.Not.ContainKey("error.type"));
        Assert.That(outcome.Tags, Does.Not.ContainKey("rpc.status_code"));
    }

    /// <summary>Verifies that a response with the <see cref="StatusCode.Ok" /> status code leaves the dispatch
    /// activity status unset and records the status code.</summary>
    [Test]
    public async Task Dispatch_activity_records_ok_response()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));

        ActivityOutcome? outcome = null;
        using var activitySource = new ActivitySource("Test Activity Source");
        using ActivityListener mockActivityListener = CreateMockActivityListener(
            activitySource,
            activity => outcome = ActivityOutcome.From(activity));
        var sut = new TelemetryMiddleware(dispatcher, activitySource);

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Operation = "Op",
            Path = "/"
        };

        // Act
        await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Status, Is.EqualTo(ActivityStatusCode.Unset));
        Assert.That(outcome.StatusDescription, Is.Null);
        Assert.That(outcome.Tags, Does.ContainKey("rpc.status_code").WithValue("Ok"));
        Assert.That(outcome.Tags, Does.Not.ContainKey("error.type"));
    }

    /// <summary>The exceptions thrown by the dispatch that the caller receives as a status code that reports a server
    /// failure, with this status code, the expected error type and the expected error message. Like a returned
    /// response, an exception is identified by the status code of the failure response the caller receives. The
    /// middleware is called with <see cref="CancellationToken.None" />, which cannot be canceled, so both
    /// cancellations are internal errors.</summary>
    private static IEnumerable<TestCaseData> ServerErrorExceptions
    {
        get
        {
            yield return new TestCaseData(
                new DispatchException(StatusCode.InternalError, "dispatch failed"),
                "InternalError",
                "InternalError",
                "dispatch failed");
            yield return new TestCaseData(
                new DispatchException((StatusCode)42, "dispatch failed"),
                "42",
                "_OTHER",
                "dispatch failed");
            yield return new TestCaseData(
                new DispatchException(StatusCode.Unauthorized, "dispatch failed") { ConvertToInternalError = true },
                "InternalError",
                "InternalError",
                "The dispatch failed with status code InternalError. The failure was caused by an exception of type 'IceRpc.DispatchException' with message: dispatch failed");
            yield return new TestCaseData(
                new NotSupportedException("not supported"),
                "NotSupported",
                "NotSupported",
                "The dispatch failed with status code NotSupported. The failure was caused by an exception of type 'System.NotSupportedException' with message: not supported");
            yield return new TestCaseData(
                new InvalidOperationException("dispatch failed"),
                "InternalError",
                "InternalError",
                "The dispatch failed with status code InternalError. The failure was caused by an exception of type 'System.InvalidOperationException' with message: dispatch failed");
            yield return new TestCaseData(
                new OperationCanceledException("dispatch canceled"),
                "InternalError",
                "InternalError",
                "The dispatch failed with status code InternalError. The failure was caused by an exception of type 'System.OperationCanceledException' with message: dispatch canceled");
            yield return new TestCaseData(
                new OperationCanceledException("dispatch canceled", new CancellationToken(canceled: true)),
                "InternalError",
                "InternalError",
                "The dispatch failed with status code InternalError. The failure was caused by an exception of type 'System.OperationCanceledException' with message: dispatch canceled");
        }
    }

    /// <summary>The exceptions thrown by the dispatch that the caller receives as a status code that reports a problem
    /// with the request, with this status code.</summary>
    private static IEnumerable<TestCaseData> RequestErrorExceptions
    {
        get
        {
            yield return new TestCaseData(
                new DispatchException(StatusCode.Unauthorized, "dispatch failed"),
                "Unauthorized");
            yield return new TestCaseData(new InvalidDataException("invalid data"), "InvalidData");
            yield return new TestCaseData(
                new IceRpcException(IceRpcError.TruncatedData, "truncated data"),
                "TruncatedPayload");
        }
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

    /// <summary>The status and tags of an activity, captured when the activity stops.</summary>
    private sealed record class ActivityOutcome(
        ActivityStatusCode Status,
        string? StatusDescription,
        IReadOnlyDictionary<string, object?> Tags)
    {
        internal static ActivityOutcome From(Activity activity) =>
            new(
                activity.Status,
                activity.StatusDescription,
                activity.TagObjects.ToDictionary(entry => entry.Key, entry => entry.Value));
    }
}
