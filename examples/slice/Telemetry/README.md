# Telemetry

This application illustrates how to use the IceRPC telemetry interceptor and middleware, and how they can be integrated
with OpenTelemetry to export traces over the OpenTelemetry Protocol (OTLP). The application also shows how the trace
context is propagated from the client to the server, by just configuring the IceRPC telemetry interceptor and
middleware.

You can build the client and server applications with:

``` shell
dotnet build
```

The example sends traces to an OTLP endpoint at `http://localhost:4317` (the default for `AddOtlpExporter`). Any
OTLP-compatible collector or backend works; the simplest option is the Jaeger v2 single-binary container, which
natively accepts OTLP and provides a web UI:

```shell
docker run --rm --name jaeger -p 4317:4317 -p 16686:16686 jaegertracing/jaeger:2.17.0
```

In a separate terminal start the Greeter Server program:

```shell
cd Server
dotnet run
```

In a separate terminal, start the Client program:

```shell
cd Client
dotnet run
```

The trace information should now be available in the Jaeger UI:

- <http://localhost:16686>
