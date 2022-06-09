This application illustrates how to use the IceRpc telemetry interceptor and middleware, and how they can be
integrated with OpenTelemetry to export traces to Zipkin.

The application shows how the trace context is propagated from the client to the front end Hello server, and
from the front end Hello server to the backend Customer server, by just configuring the IceRpc telemetry interceptor
and middleware.

For build instructions check the top-level [README.md](../../README.md).

Firt start the Zipkin service as documented in the Zipkin quick start guide:

- https://zipkin.io/pages/quickstart.html

In a separate window start the Customer Server program:
```
dotnet run --project CustomerServer/CustomerServer.csproj
```

In a separate window start the Hello Server program:
```
dotnet run --project HelloServer/HelloServer.csproj
```

In a separate window, start the Client program:
```
dotnet run --project Client/Client.csproj
```

The trace information should now be available in the Zipkin local service:

 - http://localhost:9411/zipkin

[Zipkin](./zipkin.png)
