// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Slice;
using System.Security.Cryptography.X509Certificates;
using static Program;

namespace IceRpc.Compressor.Examples;

public static class CompressorMiddlewareExamples
{
    public static async Task UseCompressor()
    {
        #region UseCompressor
        // Add the compressor middleware to the dispatch pipeline.
        Router router = new Router()
            .UseCompressor(CompressionFormat.Brotli)
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
