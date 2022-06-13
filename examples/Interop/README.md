This example application illustrates how IceRPC can interact with ZeroC Ice using ice protocol and Slice 1 encoding,
the server application uses ZeroC Ice and the client application uses IceRPC.

For build instructions check the top-level [README.md](../../README.md).

First start the Server program:
```
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client program:
```
dotnet run --project Client/Client.csproj
```
