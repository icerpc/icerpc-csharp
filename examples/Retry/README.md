# Retry

This application illustrates how to use the retry interceptor to retry failed invocations and make the application
resilient to failures.

The server is configured to randomly fail with `DispatchException(StatusCode.Unavailable)`, which can only be retried
on a different server address. The retry interceptor will automatically retry failed invocations up to the configured
max attempts. If the retry interceptor reaches the max retry attempts, or if it exhausted all available server
addresses, it gives up on retrying and reports the failure.

You can build the client and server applications with:

``` shell
dotnet build
```

First start at least two instances of the Server:

```shell
cd Server
dotnet run -- 0
```

In a separate window, start the second instance:

```shell
cd Server
dotnet run -- 1
```

You can start additional instances of the Server, using consecutive numbers:

```shell
cd Server
dotnet run -- 2
```

In a separate window, start the Client program, passing the number of server instances as an argument:

```shell
cd Client
dotnet run -- 3
```

The client will continue sending invocations until you stop it with Ctrl+C, or until the invocation fails because the
retry interceptor cannot recover from the failure, which can happen if the retry interceptor reaches the max retry
attempts, or if it has exhausted all available server addresses.

You can also stop some of the servers while the client is running. The client will keep sending invocations unless all
remaining servers throw `DispatchException(StatusCode.Unavailable)`.
