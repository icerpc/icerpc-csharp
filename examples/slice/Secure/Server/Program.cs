// Copyright (c) ZeroC, Inc.

using IceRpc;
using SecureServer;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Create the authentication options using the test server certificate.
var serverAuthenticationOptions = new SslServerAuthenticationOptions()
{
    ServerCertificateContext = SslStreamCertificateContext.Create(
        X509CertificateLoader.LoadPkcs12FromFile(
            "../../../../certs/server.p12",
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable),
        additionalCertificates: null)
};

await using var server = new Server(new Chatbot(), serverAuthenticationOptions);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
