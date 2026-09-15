// Copyright (c) ZeroC, Inc.

namespace IceRpc.Telemetry.Internal;

/// <summary>Provides an extension method for <see cref="StatusCode" /> to identify a failure in the
/// <c>error.type</c> tag of an activity.</summary>
internal static class StatusCodeExtensions
{
    /// <summary>Gets the <c>error.type</c> of a failure status code. <see cref="StatusCode" /> is an unchecked enum, so
    /// an undefined value maps to <c>_OTHER</c> to keep the cardinality of <c>error.type</c> bounded.</summary>
    /// <param name="statusCode">The failure status code.</param>
    /// <returns>The name of <paramref name="statusCode" /> when it is defined; otherwise, <c>_OTHER</c>.</returns>
    internal static string ToErrorType(this StatusCode statusCode) =>
        Enum.IsDefined(statusCode) ? statusCode.ToString() : "_OTHER";
}
