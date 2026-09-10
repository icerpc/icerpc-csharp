// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using static Program;

namespace IceRpc.Telemetry.Examples;

// This class provides code snippets used by the doc-comments of the telemetry interceptor.
public static class TelemetryInterceptorExamples
{
    public static async Task UseTelemetry()
    {
        #region UseTelemetry
        // The activity source used by the telemetry interceptor.
        using var activitySource = new ActivitySource("IceRpc");

        // The server uses a test certificate, so we trust its root CA. CreateClientAuthenticationOptions
        // is a helper from examples/common/Program.Authentication.cs in the icerpc-csharp repo.
        using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

        await using var connection = new ClientConnection(
            new Uri("icerpc://localhost"),
            clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

        // Create an invocation pipeline and install the telemetry interceptor.
        Pipeline pipeline = new Pipeline()
            .UseTelemetry(activitySource)
            .Into(connection);
        #endregion
    }
}
