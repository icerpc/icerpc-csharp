// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Slice;

namespace IceRpc.Metrics.Examples;

// This class provides code snippets used by the doc-comments of the metrics middleware.
public static class MetricsMiddlewareExamples
{
    public static async Task UseMetrics()
    {
        #region UseMetrics
        // Create a router (dispatch pipeline) and install the metrics middleware.
        Router router = new Router()
            .UseMetrics()
            .Map<IGreeterService>(new Chatbot());

        await using var server = new Server(router);
        server.Listen();
        #endregion
    }
}
