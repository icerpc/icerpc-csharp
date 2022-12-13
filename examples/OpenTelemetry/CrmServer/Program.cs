// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

// The activity source used by the telemetry middleware.
using var activitySource = new ActivitySource("IceRpc");

// Add the telemetry middleware to the dispatch pipeline.
Router router = new Router().UseTelemetry(activitySource);

// Configure OpenTelemetry trace provider to subscribe to the activity source used by the IceRPC telemetry middleware,
// and to export the traces to the Zipkin service.
using TracerProvider tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("CRM Server"))
   .AddZipkinExporter()
   .Build();

router.Map<ICrm>(new Crm());

await using var server = new Server(router, new Uri("icerpc://127.0.0.1:20001"));

// Shuts down the server on Ctrl+C.
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
