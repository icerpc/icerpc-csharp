# Retry

This application illustrates how to use the retry interceptor to retry failed invocations and make the application
resilient to failures.

The server is configured to randomly fail, and the interceptor will automatically retry failed invocations up to the
configured max attempts. If the interceptor reaches the max attempts, it gives up on retrying and reports the failure.

If the status code of the response is `Unavailable`, the current server address is excluded from subsequent attempts
and the client only retries when additional server addresses are configured.

First start at least two instances of the Server:

```shell
dotnet run --project Server/Server.csproj -- 0
```

In a separate window, start the second instance:

```shell
dotnet run --project Server/Server.csproj -- 1
```

You can start additional instances of the Server, using consecutive numbers:

```shell
dotnet run --project Server/Server.csproj -- 2
```

In a separate window, start the Client program, passing the number of server instances as an argument:

```shell
dotnet run --project Client/Client.csproj -- 3
```

Try stopping the first server instance, this will cause the invocations to retry on the second server instance.

The client will continue sending invocations until you stop it with Ctrl+C.
