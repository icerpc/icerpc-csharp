// Copyright (c) ZeroC, Inc.

using GreeterSecureServer;
using IceRpc;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Create the authentication options with our server certificate.
var serverAuthenticationOptions = new SslServerAuthenticationOptions()
{
    ServerCertificate = new X509Certificate2("../../certs/server.p12", "password")
};

await using var server = new Server(new Chatbot(), serverAuthenticationOptions);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
