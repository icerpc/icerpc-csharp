// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;
using QuicServer;

// Create a server that uses the test server certificate, and the QUIC multiplexed transport.
await using var server = new Server(
    new Chatbot(),
    serverAuthenticationOptions: CreateServerAuthenticationOptions(),
    multiplexedServerTransport: new QuicServerTransport());

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
