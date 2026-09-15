// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

/// <summary>Provides a helper method to compose the error message of a failure response.</summary>
internal static class DispatchErrorMessage
{
    /// <summary>Composes the error message of a failure response.</summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="exception">The exception that is the cause of the failure, or <see langword="null" /> when the
    /// failure was not caused by an exception.</param>
    /// <returns><paramref name="message" /> when <paramref name="exception" /> is <see langword="null" />; otherwise,
    /// <paramref name="message" /> followed by the type and the message of <paramref name="exception" />.</returns>
    internal static string Compose(string message, Exception? exception) =>
        exception is null ?
            message :
            $"{message} The failure was caused by an exception of type '{exception.GetType()}' with message: {exception.Message}";
}
