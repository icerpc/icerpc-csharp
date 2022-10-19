// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Transports;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

var hello = new Hello();
await using var server = new Server(
    new ServerOptions
    {
        ServerAuthenticationOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = new X509Certificate2("../../certs/server.p12", "password")
        },
        ConnectionOptions = new ConnectionOptions
        {
            Dispatcher = hello
        }
    },
    multiplexedServerTransport: new QuicServerTransport());



// Shuts down the server on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
