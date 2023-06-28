# GreeterCore

The GreeterCore example illustrates how to send a request and wait for the response with the core API. It doesn't use
Slice or generated code.

The "contract" between the client and the server is in the application code: when the server dispatches a request for
operation `greet`, its dispatcher knows the exact format of the request payload (knowledge shared with the client that
created the request). Likewise, the client and the dispatcher agree on the format of the response payload for `greet`.

You can build the client and server applications with:

``` shell
dotnet build
```

First start the Server program:

```shell
cd Server
dotnet run
```

In a separate window, start the Client program:

```shell
cd Client
dotnet run
```
