// Copyright (c) ZeroC, Inc.

using IceRpc;
using RetryServer;
using System.Security.Cryptography.X509Certificates;

if (args.Length < 1)
{
    Console.WriteLine("Missing server number argument");
    return;
}

int number;
if (!int.TryParse(args[0], out number))
{
    Console.WriteLine($"Invalid server number argument '{args[0]}', expected a number");
    return;
}

// The default transport (QUIC) requires a server certificate. We use a test certificate here.
using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

var serverAddress = new ServerAddress(new Uri($"icerpc://[::0]:{10000 + number}/"));

await using var server = new Server(
    new Chatbot(number),
    serverAddress,
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
