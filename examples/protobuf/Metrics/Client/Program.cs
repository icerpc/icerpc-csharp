// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Load the root CA certificate.
using var rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));
Pipeline pipeline = new Pipeline().UseMetrics().Into(connection);

var greeter = new GreeterClient(pipeline);

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
    await greeter.GreetAsync(new GreetRequest { Name = Environment.UserName });
}

await connection.ShutdownAsync();
