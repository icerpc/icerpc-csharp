// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace IceRpc.Telemetry.Internal;

/// <summary>Provides extension methods for <see cref="Activity" /> that record the outcome of an invocation or a
/// dispatch, following the OpenTelemetry semantic conventions for RPC spans.</summary>
internal static class ActivityExtensions
{
    /// <summary>Records the status code of a response. A status code other than <see cref="StatusCode.Ok" /> marks
    /// the activity as failed.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="statusCode">The status code of the response.</param>
    /// <param name="errorMessage">The error message of the response.</param>
    internal static void RecordStatusCode(this Activity activity, StatusCode statusCode, string? errorMessage)
    {
        string statusCodeName = statusCode.ToString();
        activity.SetTag("rpc.response.status_code", statusCodeName);
        if (statusCode != StatusCode.Ok)
        {
            activity.SetTag("error.type", statusCodeName);
            activity.SetStatus(ActivityStatusCode.Error, errorMessage);
        }
    }

    /// <summary>Records an exception thrown by the invocation or the dispatch, which marks the activity as failed.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="exception">The exception.</param>
    internal static void RecordException(this Activity activity, Exception exception)
    {
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }
}
