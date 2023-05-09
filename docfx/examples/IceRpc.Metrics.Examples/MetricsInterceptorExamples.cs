// Copyright (c) ZeroC, Inc.

namespace IceRpc.Metrics.Examples;

// This class provides code snippets used by the doc-comments of the metrics interceptor.
public static class MetricsInterceptorExamples
{
    public static async Task UseMetrics()
    {
        #region UseMetrics
        // Create a client connection.
        await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

        // Create an invocation pipeline and install the metrics interceptor.
        Pipeline pipeline = new Pipeline()
            .UseMetrics()
            .Into(connection);
        #endregion
    }
}
