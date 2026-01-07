// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure connection to the server using the default transport (QUIC).
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

// Add the request context interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline().UseRequestContext().Into(connection);

var greeter = new GreeterProxy(pipeline);

// Create a feature collection holding an IRequestContextFeature.
IFeatureCollection features = new FeatureCollection().With<IRequestContextFeature>(
    new RequestContextFeature
    {
        ["UserId"] = Environment.UserName.ToLowerInvariant(),
        ["MachineName"] = Environment.MachineName
    });

// The request context interceptor encodes the request context feature into the request context field.
string greeting = await greeter.GreetAsync(Environment.UserName, features);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
