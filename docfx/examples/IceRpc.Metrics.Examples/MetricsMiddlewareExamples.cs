// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Slice;
using System.Security.Cryptography.X509Certificates;
using static Program;

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
            .Map(new Chatbot());

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
