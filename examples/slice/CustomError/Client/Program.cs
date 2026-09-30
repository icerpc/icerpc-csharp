// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;
using ZeroC.Slice; // for the Result<TSuccess, TFailure> generic type

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure connection to the server using the default transport (QUIC).
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

var greeter = new GreeterProxy(connection);

string[] names = ["", "jimmy", "billy bob", "alice", Environment.UserName];

foreach (string name in names)
{
    Result<string, GreeterError> result = await greeter.GreetAsync(name);

    // Use the Dunet-generated Match method to process the result, and a switch expression to process the GreeterError.
    string message = result.Match(
        success => success.Value,
        failure => failure.Value switch
        {
            GreeterError.Away away => $"Away until {away.Until.ToLocalTime()}",
            _ => $"{failure.Value}",
        });

    Console.WriteLine($"The greeting for '{name}' is '{message}'");
}

await connection.ShutdownAsync();
