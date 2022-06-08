// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

// Create an invocation pipeline that uses the telemetry interceptor.
using var activitySource = new ActivitySource("IceRpc");
var pipeline = new Pipeline().UseTelemetry(activitySource);

// Configure OpenTelemetry trace provider to subscribe to the activity source used by IceRpc telemetry interceptor,
// and to export the traces to Zipkin service.
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Hello Client"))
   .AddZipkinExporter()
   .Build();

await using var connection = new ClientConnection("icerpc://127.0.0.1");

IHelloPrx hello = HelloPrx.FromConnection(connection, "/hello", pipeline);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
