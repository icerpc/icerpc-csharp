// Copyright (c) ZeroC, Inc.

using IceRpc;
using OnewayServer;
using System.Security.Cryptography.X509Certificates;

// The default transport (QUIC) requires a server certificate. We use a test certificate here.
using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Create a server that dispatches all requests to the same service, an instance of Scribe.
await using var server = new Server(
    new Scribe(),
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
