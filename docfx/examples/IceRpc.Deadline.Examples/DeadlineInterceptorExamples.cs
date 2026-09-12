// Copyright (c) ZeroC, Inc.

using System.Security.Cryptography.X509Certificates;
using static Program;

namespace IceRpc.Deadline.Examples;

// This class provides code snippets used by the doc-comments of the deadline interceptor.
public static class DeadlineInterceptorExamples
{
    public static async Task UseDeadline()
    {
        #region UseDeadline
        // The server uses a test certificate, so we trust its root CA. CreateClientAuthenticationOptions
        // is a helper from examples/common/Program.Authentication.cs in the icerpc-csharp repo.
        using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

        await using var connection = new ClientConnection(
            new Uri("icerpc://localhost"),
            clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

        // Create an invocation pipeline, that uses the deadline interceptor.
        Pipeline pipeline = new Pipeline()
            .UseDeadline()
            .Into(connection);
        #endregion
    }

    public static async Task UseDeadlineWithDefaultTimeout()
    {
        #region UseDeadlineWithDefaultTimeout
        // The server uses a test certificate, so we trust its root CA. CreateClientAuthenticationOptions
        // is a helper from examples/common/Program.Authentication.cs in the icerpc-csharp repo.
        using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

        await using var connection = new ClientConnection(
            new Uri("icerpc://localhost"),
            clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

        // Create an invocation pipeline, that uses the deadline interceptor and has a default
        // timeout of 500 ms.
        Pipeline pipeline = new Pipeline()
            .UseDeadline(defaultTimeout: TimeSpan.FromMilliseconds(500))
            .Into(connection);
        #endregion
    }
}
