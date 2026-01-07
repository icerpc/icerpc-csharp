// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using TcpServer;

// Create a server that listens for TCP connections.
// Since TcpServerTransport is a multiplexed transport, we wrap it in a SlicServerTransport to get a multiplexed
// transport.
await using var server = new Server(
    new Chatbot(),
    multiplexedServerTransport: new SlicServerTransport(new TcpServerTransport()));

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
