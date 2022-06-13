This application illustrate how to use IceRpc retry interceptor to retry failed invocations and make the application
resilent to failures.

The server is configured to randomly fail, and the client will automatically retry failed invocations up to the configured
max attempts. If the client invocation reaches the max attempts it gives up on retry and report the failure, if the server
failure carries a RetryPolicy.OtherReplica the current endpoint will be excluded for following attempts and the client will
be only able to retry if additional endpoints were configured.

First start at least two instances of the Server:
```
dotnet run --project Server/Server.csproj -- 0
```

In a separate window, start the second instance:
```
dotnet run --project Server/Server.csproj -- 1
```

You can start additional instances of the Server, using consecutive numbers:
```
dotnet run --project Server/Server.csproj -- 2
```

In a separate window, start the Client program, passing the number of server instances as an argument:
```
dotnet run --project Client/Client.csproj -- 3
```

Try stopping the first server instance, this will cause the invocations to retry on the second server instance.

The client will continue sending invocations until you stop it with Ctrl+C.
