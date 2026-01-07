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
Pipeline pipeline = new Pipeline().UseMetrics().Into(connection);

var greeter = new GreeterProxy(pipeline);

double requestsPerSecond = 20;
Console.WriteLine($"Sending {requestsPerSecond} requests per second...");

using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1 / requestsPerSecond));

// Stop the client on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    periodicTimer.Dispose();
};

// Start invoking the remote method
while (await periodicTimer.WaitForNextTickAsync())
{
    await greeter.GreetAsync(Environment.UserName);
}

await connection.ShutdownAsync();
