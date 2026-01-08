// Copyright (c) ZeroC, Inc.

using GreeterServer;
using IceRpc;
using System.Security.Cryptography.X509Certificates;

// Load the server certificate.
using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Create a server that dispatches all requests to the same service, an instance of Chatbot.
await using var server = new Server(
    new Chatbot(),
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
