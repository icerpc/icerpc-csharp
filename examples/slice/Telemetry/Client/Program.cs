// Copyright (c) ZeroC, Inc.

using IceRpc;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using VisitorCenter;

// The activity source used by the telemetry interceptor.
using var activitySource = new ActivitySource("IceRpc");

// Configure OpenTelemetry trace provider to subscribe to the activity source used by the IceRpc telemetry interceptor,
// and to export the traces using the OpenTelemetry Protocol (OTLP).
using TracerProvider? tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Greeter Client"))
   .AddOtlpExporter()
   .Build();

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));
// Create an invocation pipeline and add the telemetry interceptor to it.
Pipeline pipeline = new Pipeline().UseTelemetry(activitySource).Into(connection);

var greeter = new GreeterProxy(pipeline);

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
