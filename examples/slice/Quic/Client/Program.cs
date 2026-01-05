// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a connection that uses the test root CA, and the QUIC multiplexed transport.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA),
    multiplexedClientTransport: new QuicClientTransport());

var greeter = new GreeterProxy(connection);
string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
