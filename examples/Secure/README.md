Secure is an example application showing how to make an asynchronous invocation and wait for the reply using a
secure TLS connection.

For build instructions check the top-level [README.md](../../README.md).

First start the Server program:

```
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client program:

```
dotnet run --project Client/Client.csproj
```
