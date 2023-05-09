# Coloc transport for IceRPC

IceRpc.Transports.Coloc is an implementation of [IceRPC][icerpc]'s duplex transport abstraction. You can use this
transport to send RPCs to services hosted by IceRPC servers in your local application.

The primary use case for this transport is testing. Several unit tests running in the same process can easily and cheaply
create their own private coloc connections.

This transport does not use network APIs. It is available on all platforms.

[Source code][source] | [Package][package] | [API reference documentation][api] | [Product documentation][product]

## Sample code

```csharp
// Create an IceRPC server with Coloc

using IceRpc;
using IceRpc.Transports.Coloc;

var coloc = new ColocTransport();

await using var server = new Server(
    dispatcher: ...,
    multiplexedServerTransport: new SlicServerTransport(coloc.ServerTransport));

server.Listen();

// Create a client connection to this server

await using var connection = new ClientConnection(
    new Uri("icerpc://host"), // you can use any host and port with coloc
    multiplexedClientTransport: new SlicClientTransport(coloc.ClientTransport));

await connection.ConnectAsync();
```

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Transports.Coloc.html
[icerpc]: https://www.nuget.org/packages/IceRpc
[package]: https://www.nuget.org/packages/IceRpc.Transports.Coloc
[product]: https://docs.testing.zeroc.com/docs/icerpc-core
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Transports.Coloc
