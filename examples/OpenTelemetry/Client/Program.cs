// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

// The activity source used by the telemetry interceptor.
using var activitySource = new ActivitySource("IceRpc");

// Create an invocation pipeline and add the telemetry interceptor to it.
var pipeline = new Pipeline().UseTelemetry(activitySource);

// Configure OpenTelemetry trace provider to subscribe to the activity source used by the IceRpc telemetry interceptor,
// and to export the traces to the Zipkin service.
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Hello Client"))
   .AddZipkinExporter()
   .Build();

await using var connection = new ClientConnection("icerpc://127.0.0.1");

pipeline.Into(connection);

var hello = new HelloPrx(connection);
hello.Proxy.Invoker = pipeline;

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
