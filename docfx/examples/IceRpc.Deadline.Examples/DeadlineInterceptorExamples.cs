// Copyright (c) ZeroC, Inc.

namespace IceRpc.Deadline.Examples;

// This class provides code snippets used by the doc-comments of the deadline interceptor.
public static class DeadlineInterceptorExamples
{
    public static async Task UseDeadline()
    {
        #region UseDeadline
        await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

        // Create an invocation pipeline, that uses the deadline interceptor.
        Pipeline pipeline = new Pipeline()
            .UseDeadline()
            .Into(connection);
        #endregion
    }

    public static async Task UseDeadlineWithDefaultTimeout()
    {
        #region UseDeadlineWithDefaultTimeout
        await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

        // Create an invocation pipeline, that uses the deadline interceptor and has a default timeout of 500 ms.
        Pipeline pipeline = new Pipeline()
            .UseDeadline(defaultTimeout: TimeSpan.FromMilliseconds(500))
            .Into(connection);
        #endregion
    }
}
