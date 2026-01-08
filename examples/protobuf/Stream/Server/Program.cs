// Copyright (c) ZeroC, Inc.

using IceRpc;
using StreamServer;
using System.Security.Cryptography.X509Certificates;

// Load the server certificate.
using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

await using var server = new Server(
    new RandomGenerator(),
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
