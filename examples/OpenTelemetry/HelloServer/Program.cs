// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetryExample;
using System.Diagnostics;

// The activity source used by the telemetry interceptor and middleware.
using var activitySource = new ActivitySource("IceRpc");

// Add the telemetry interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline().UseTelemetry(activitySource);

// Add the telemetry middleware to the dispatch pipeline.
Router router = new Router().UseTelemetry(activitySource);

// Configure OpenTelemetry trace provider to subscribe to the activity source used by the IceRPC telemetry interceptor
// and middleware, and to export the traces to the Zipkin service.
using TracerProvider tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Hello Server"))
   .AddZipkinExporter()
   .Build();

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1:20001"));
pipeline.Into(connection);

var proxy = new CrmProxy(pipeline);
router.Map<IHello>(new Hello(proxy));

await using var server = new Server(router);

// Create a task completion source to keep running until Ctrl+C is pressed.
var cancelKeyPressed = new TaskCompletionSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = cancelKeyPressed.TrySetResult();
};

server.Listen();
await cancelKeyPressed.Task;
await server.ShutdownAsync();
