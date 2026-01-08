// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Security.Cryptography.X509Certificates;
using TwoD;

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure connection to the server using the default transport (QUIC).
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

var areaCalculator = new AreaCalculatorProxy(connection);

// An array of various shapes (C# record classes).
Shape[] shapes =
{
    new Shape.Rectangle(3.0F, 2.0F),
    new Shape.Ellipse(3.0F, 2.0F),
    new Shape.Square(4.0F),
    new Shape.Circle(2.0F),
};

foreach (var shape in shapes)
{
    double area = await areaCalculator.ComputeAreaAsync(shape);
    Console.WriteLine($"The area of {shape} is {area:F2}");
}

await connection.ShutdownAsync();
