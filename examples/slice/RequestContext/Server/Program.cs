// Copyright (c) ZeroC, Inc.

using IceRpc;
using RequestContextServer;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// The default transport (QUIC) requires a server certificate. We use a test certificate here.
using X509Certificate2 serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Add the request context middleware to the dispatch pipeline.
Router router = new Router().UseRequestContext();
router.Map<IGreeterService>(new Chatbot());

// Create a server that uses the test server certificate.
await using var server = new Server(
    router,
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
