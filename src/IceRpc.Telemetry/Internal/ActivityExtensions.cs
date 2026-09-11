// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace IceRpc.Telemetry.Internal;

/// <summary>Provides extension methods for <see cref="Activity" /> that record the outcome of an invocation or a
/// dispatch, following the OpenTelemetry semantic conventions for RPC spans.</summary>
internal static class ActivityExtensions
{
    /// <summary>Records the status code of a response.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="statusCode">The status code of the response.</param>
    /// <param name="errorMessage">The error message of the response.</param>
    /// <param name="isError">Whether the status code marks the activity as failed.</param>
    internal static void RecordStatusCode(
        this Activity activity,
        StatusCode statusCode,
        string? errorMessage,
        bool isError)
    {
        string statusCodeName = statusCode.ToString();
        activity.SetTag("rpc.status_code", statusCodeName);
        if (isError)
        {
            activity.SetTag("error.type", statusCodeName);
            activity.SetStatus(ActivityStatusCode.Error, errorMessage);
        }
    }

    /// <summary>Records an exception thrown by the invocation or the dispatch, which marks the activity as failed.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="statusCode">The status code of the response the caller receives, or <see langword="null" /> when
    /// the exception produces no response.</param>
    internal static void RecordException(this Activity activity, Exception exception, StatusCode? statusCode)
    {
        if (statusCode is StatusCode value)
        {
            activity.SetTag("rpc.status_code", value.ToString());
        }
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }
}
