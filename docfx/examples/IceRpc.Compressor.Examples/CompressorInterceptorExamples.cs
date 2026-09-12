// Copyright (c) ZeroC, Inc.

using System.Security.Cryptography.X509Certificates;
using static Program;

namespace IceRpc.Compressor.Examples;

public static class CompressorInterceptorExamples
{
    public static async Task UseCompressor()
    {
        #region UseCompressor
        // The server uses a test certificate, so we trust its root CA. CreateClientAuthenticationOptions
        // is a helper from examples/common/Program.Authentication.cs in the icerpc-csharp repo.
        using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

        await using var connection = new ClientConnection(
            new Uri("icerpc://localhost"),
            clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

        // Create an invocation pipeline and install the compressor interceptor.
        Pipeline pipeline = new Pipeline()
            .UseCompressor(CompressionFormat.Brotli)
            .Into(connection);
        #endregion
    }
}
