// Copyright (c) ZeroC, Inc.

using GreeterQuicExample;
using IceRpc;
using IceRpc.Transports.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

await using var server = new Server(
    new Chatbot(),
    new SslServerAuthenticationOptions
    {
        ServerCertificate = new X509Certificate2("../../certs/server.p12", "password")
    },
    multiplexedServerTransport: new QuicServerTransport());

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
