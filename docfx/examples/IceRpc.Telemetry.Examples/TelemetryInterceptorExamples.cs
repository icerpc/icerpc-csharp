// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace IceRpc.Telemetry.Examples;

// This class provides code snippets used by the doc-comments of the telemetry interceptor.
public static class TelemetryInterceptorExamples
{
    public static async Task UseTelemetry()
    {
        #region UseTelemetry
        // The activity source used by the telemetry interceptor.
        using var activitySource = new ActivitySource("IceRpc");

        // Create a connection.
        await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

        // Create an invocation pipeline and install the telemetry interceptor.
        Pipeline pipeline = new Pipeline()
            .UseTelemetry(activitySource)
            .Into(connection);
        #endregion
    }
}
