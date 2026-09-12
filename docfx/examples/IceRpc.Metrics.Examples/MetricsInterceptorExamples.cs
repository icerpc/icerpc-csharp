// Copyright (c) ZeroC, Inc.

using System.Security.Cryptography.X509Certificates;
using static Program;

namespace IceRpc.Metrics.Examples;

// This class provides code snippets used by the doc-comments of the metrics interceptor.
public static class MetricsInterceptorExamples
{
    public static async Task UseMetrics()
    {
        #region UseMetrics
        // The server uses a test certificate, so we trust its root CA. CreateClientAuthenticationOptions
        // is a helper from examples/common/Program.Authentication.cs in the icerpc-csharp repo.
        using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

        await using var connection = new ClientConnection(
            new Uri("icerpc://localhost"),
            clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

        // Create an invocation pipeline and install the metrics interceptor.
        Pipeline pipeline = new Pipeline()
            .UseMetrics()
            .Into(connection);
        #endregion
    }
}
