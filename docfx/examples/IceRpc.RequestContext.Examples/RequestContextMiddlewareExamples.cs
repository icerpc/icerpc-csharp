// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Slice;

namespace IceRpc.RequestContext.Examples;

// This class provides code snippets used by the doc-comments of the request context middleware.
public static class MetricsMiddlewareExamples
{
    public static async Task UseRequestContext()
    {
        #region UseRequestContext
        // Create a router (dispatch pipeline) and install the request context middleware.
        Router router = new Router()
            .UseRequestContext()
            .Map<IGreeterService>(new Chatbot());

        await using var server = new Server(router);
        server.Listen();
        #endregion
    }
}
