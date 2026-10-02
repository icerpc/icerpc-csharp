// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;
using ZeroC.Slice; // for Result, Success and Failure

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
    // Passing the cancellation token explicitly works around a .NET 11 RC1 compiler bug (CS8655 on the switch below).
    // See https://github.com/dotnet/roslyn/issues/85852.
    Result<string, GreeterError> result = await greeter.GreetAsync(name, cancellationToken: CancellationToken.None);

    string message = result switch
    {
        Success<string>(var greeting) => greeting,
        Failure<GreeterError>(var error) => error switch
        {
            GreeterError.Away away => $"Away until {away.Until.ToLocalTime()}",
            _ => $"{error}",
        },
    };

    Console.WriteLine($"The greeting for '{name}' is '{message}'");
}

await connection.ShutdownAsync();
