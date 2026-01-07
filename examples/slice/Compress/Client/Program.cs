// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure connection to the server using the default transport (QUIC).
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

// Add the compressor interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline().UseCompressor(CompressionFormat.Brotli).Into(connection);

// Create a proxy using the invocation pipeline.
var greeter = new GreeterProxy(pipeline);

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
