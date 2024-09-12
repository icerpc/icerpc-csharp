// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;
using QuicServer;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Create a server that uses the test server certificate, and the QUIC multiplexed transport.
await using var server = new Server(
    new Chatbot(),
    new SslServerAuthenticationOptions
    {
        ServerCertificateContext = SslStreamCertificateContext.Create(
            X509CertificateLoader.LoadPkcs12FromFile(
                "../../../../certs/server.p12",
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable),
            additionalCertificates: null)
    });

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
