This example application illustrates how to use IceRpc telemetry interceptor and middleware, and how they can be
integrated with OpenTelemetry to export traces to Zipkin.

For build instructions checks the top-level [README.md](../../README.md).

Firt start the Zipkin service as documented in the Zipkin quick start guide:

- https://zipkin.io/pages/quickstart.html

Then start the Server program:
```
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client program:
```
dotnet run --project Client/Client.csproj
```

The trace information should now be available in the Zipkin local service:

 - http://localhost:9411/zipkin
