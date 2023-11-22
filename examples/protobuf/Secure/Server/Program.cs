// Copyright (c) ZeroC, Inc.

using IceRpc;
using SecureServer;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Create the authentication options using the test server certificate.
var serverAuthenticationOptions = new SslServerAuthenticationOptions()
{
    ServerCertificate = new X509Certificate2("../../../../certs/server.p12")
};

await using var server = new Server(new Chatbot(), serverAuthenticationOptions);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
