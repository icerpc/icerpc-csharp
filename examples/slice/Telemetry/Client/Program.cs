// Copyright (c) ZeroC, Inc.

using IceRpc;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// The activity source used by the telemetry interceptor.
using var activitySource = new ActivitySource("IceRpc");

// Configure OpenTelemetry trace provider to subscribe to the activity source used by the IceRpc telemetry interceptor,
// and to export the traces to the Zipkin service.
using TracerProvider? tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Greeter Client"))
   .AddZipkinExporter()
   .Build();

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure connection to the server using the default transport (QUIC).
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));
// Create an invocation pipeline and add the telemetry interceptor to it.
Pipeline pipeline = new Pipeline().UseTelemetry(activitySource).Into(connection);

var greeter = new GreeterProxy(pipeline);

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
