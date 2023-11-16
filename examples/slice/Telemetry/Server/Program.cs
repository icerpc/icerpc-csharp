// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Slice;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using TelemetryServer;
using VisitorCenter;

// The activity source used by the telemetry interceptor and middleware.
using var activitySource = new ActivitySource("IceRpc");

// Configure OpenTelemetry trace provider to subscribe to the activity source used by the IceRPC telemetry middleware,
// and to export the traces to the Zipkin service.
using TracerProvider? tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Greeter Server"))
   .AddZipkinExporter()
   .Build();

// Add the telemetry middleware to the dispatch pipeline.
Router router = new Router().UseTelemetry(activitySource);
router.Map<IGreeterService>(new Chatbot());

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
