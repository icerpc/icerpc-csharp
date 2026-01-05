// Copyright (c) ZeroC, Inc.

using IceRpc;
using SecureServer;
using System.Security.Cryptography.X509Certificates;

using X509Certificate2 serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Create a server that uses the test server certificate.
await using var server = new Server(
    new Chatbot(),
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
