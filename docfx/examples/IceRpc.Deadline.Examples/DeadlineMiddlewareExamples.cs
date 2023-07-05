// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Slice;

namespace IceRpc.Deadline.Examples;

// This class provides code snippets used by the doc-comments of the deadline middleware.
public static class DeadlineMiddlewareExamples
{
    public static async Task UseDeadline()
    {
        #region UseDeadline
        // Create a router (dispatch pipeline) and install the deadline middleware.
        Router router = new Router()
            .UseDeadline()
            .Map<IGreeterService>(new Chatbot());

        await using var server = new Server(router);
        server.Listen();
        #endregion
    }
}
