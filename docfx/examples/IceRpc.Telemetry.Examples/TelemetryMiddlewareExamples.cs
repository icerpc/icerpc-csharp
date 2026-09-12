// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using static Program;

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

        // The default transport (QUIC) requires a server certificate. CreateServerAuthenticationOptions
        // is a helper from examples/common/Program.Authentication.cs in the icerpc-csharp repo.
        using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
            "certs/server.p12",
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable);

        await using var server = new Server(
            router,
            serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));
        server.Listen();
        #endregion
    }
}
