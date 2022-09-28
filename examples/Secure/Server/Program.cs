// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Create the authentication options with our server certificate.
var authenticationOptions = new SslServerAuthenticationOptions()
{
    ServerCertificate = new X509Certificate2("../../certs/server.p12", "password")
};

await using var server = new Server(new Hello(), authenticationOptions);

// Shuts down the server on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

await server.ListenAsync();
await server.ShutdownComplete;
