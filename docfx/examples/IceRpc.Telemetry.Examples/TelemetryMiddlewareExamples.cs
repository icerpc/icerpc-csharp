// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace IceRpc.Telemetry.Examples;

// This class provides code snippets used by the doc-comments of the telemetry middleware.
public static class TelemetryMiddlewareExamples
{
    public static async Task UseTelemetry()
    {
        #region UseTelemetry
        // The activity source used by the telemetry middleware.
        using var activitySource = new ActivitySource("IceRpc");

        // Add the telemetry middleware to the dispatch pipeline.
        Router router = new Router()
            .UseTelemetry(activitySource);

        await using var server = new Server(router);
        server.Listen();
        #endregion
    }
}
