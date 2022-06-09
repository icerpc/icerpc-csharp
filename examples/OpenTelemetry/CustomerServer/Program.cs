// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

using var activitySource = new ActivitySource("IceRpc");

// Create an dispatch pipeline that uses the telemetry middleware.
var router = new Router().UseTelemetry(activitySource);

// Configure OpenTelemetry trace provider to subscribe to the activity source used by the IceRpc telemetry interceptor
// and middleware, and to export the traces to the Zipkin service.
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
   .AddSource(activitySource.Name)
   .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Customer Server"))
   .AddZipkinExporter()
   .Build();

router.Map("/customers", new CustomerList());

await using var server = new Server(
    new ServerOptions
    {
        Endpoint = "icerpc://127.0.0.1:20001",
        ConnectionOptions = new ConnectionOptions
        {
            Dispatcher = router
        }
    });

// Destroy the server on Ctrl+C or Ctrl+Break
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
