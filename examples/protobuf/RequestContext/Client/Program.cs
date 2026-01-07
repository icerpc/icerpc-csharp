// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Load the root CA certificate.
using var rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

// Add the request context interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline().UseRequestContext().Into(connection);

var greeter = new GreeterClient(pipeline);

// Create a feature collection holding an IRequestContextFeature.
IFeatureCollection features = new FeatureCollection().With<IRequestContextFeature>(
    new RequestContextFeature
    {
        ["UserId"] = Environment.UserName.ToLowerInvariant(),
        ["MachineName"] = Environment.MachineName
    });

// The request context interceptor encodes the request context feature into the request context field.
var request = new GreetRequest { Name = Environment.UserName };
GreetResponse response = await greeter.GreetAsync(request, features);

Console.WriteLine(response.Greeting);

await connection.ShutdownAsync();
