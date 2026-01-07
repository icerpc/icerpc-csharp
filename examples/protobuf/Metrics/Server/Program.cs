// Copyright (c) ZeroC, Inc.

using IceRpc;
using MetricsServer;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Load the server certificate.
using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Add the metrics middleware to the dispatch pipeline.
Router router = new Router().UseMetrics();

router.Map<IGreeterService>(new Chatbot());

await using var server = new Server(
    router,
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
