// Copyright (c) ZeroC, Inc.

using IceRpc;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetryExample;
using System.Diagnostics;

// The activity source used by the telemetry interceptor.
using var activitySource = new ActivitySource("IceRpc");

// Create an invocation pipeline and add the telemetry interceptor to it.
Pipeline pipeline = new Pipeline().UseTelemetry(activitySource);

// Configure OpenTelemetry trace provider to subscribe to the activity source used by the IceRpc telemetry interceptor,
// and to export the traces to the Zipkin service.
using TracerProvider tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Hello Client"))
   .AddZipkinExporter()
   .Build();

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

pipeline.Into(connection);

var hello = new HelloProxy(pipeline);

string greeting = await hello.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);
