// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

// The activity source used by the telemetry interceptor and middleware.
using var activitySource = new ActivitySource("IceRpc");

// Create an invocation pipeline and add the telemetry interceptor to it.
var pipeline = new Pipeline().UseTelemetry(activitySource);

// Create a dispatch pipeline and add the telemetry middleware to it.
var router = new Router().UseTelemetry(activitySource);

// Configure OpenTelemetry trace provider to subscribe to the activity source used by the IceRpc telemetry interceptor
// and middleware, and to export the traces to the Zipkin service.
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Hello Server"))
   .AddZipkinExporter()
   .Build();

await using var connection = new ClientConnection("icerpc://127.0.0.1:20001");
pipeline.Into(connection);

var prx = new CRMPrx(connection);
prx.Proxy.Invoker = pipeline;
router.Map<IHello>(new Hello(prx));

await using var server = new Server(router);

// Shuts down the server on Ctrl+C or Ctrl+Break
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
