// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc;
using Metrics;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using var rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

var greeter = new GreeterClient(connection);
var requestCounter = new RequestCounterClient(connection);

GreetResponse greetResponse = await greeter.GreetAsync(new GreetRequest { Name = Environment.UserName });

Console.WriteLine(greetResponse.Greeting);

GetRequestCountResponse requestCountResponse = await requestCounter.GetRequestCountAsync(new Empty());

Console.WriteLine($"requests count: {requestCountResponse.Count}");

await connection.ShutdownAsync();
