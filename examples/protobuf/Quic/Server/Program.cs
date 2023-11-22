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
        ServerCertificate = new X509Certificate2("../../../../certs/server.p12")
    },
    multiplexedServerTransport: new QuicServerTransport());

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
