This application ilustrate how to use IceRpc retry interceptor to retry failed invocations and make the application
resilent to failures.

The server is configured to randomly fail, and the client will automatically retry failed invocations up to the configured
max retries.

First start two instances Server program:
```
dotnet run --project Server\Server.csproj -- icerpc://127.0.0.1:10000
```

In a separate window, start the second instance
```
dotnet run --project Server\Server.csproj -- icerpc://127.0.0.1:10001
```

In a separate window, start the Client program:
```
dotnet run --project Client/Client.csproj
```

Try stoping the first server instance, this will cause the invocations to retry on the second server instance.

The client will continue sending invocations until you stop it with Ctrl+C.
