# QUIC Transport for IceRPC

IceRpc.Quic allows you to use QUIC with [IceRPC][icerpc]. It's a thin layer over [`System.Net.Quic`][quic].

QUIC is a new UDP-based multiplexed transport used by HTTP/3 and several other application protocols.

[Source code][source] | [Package][package] | [Example][example] | [API reference documentation][api] | [Product documentation][product]

## Sample code

```csharp
// Create an IceRPC client connection with QUIC

using IceRpc;
using IceRpc.Transports;
using System.Net.Security;

await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    new SslClientAuthenticationOptions { ... },
    multiplexedClientTransport: new QuicClientTransport());
```

```csharp
// Create an IceRPC server with QUIC

using IceRpc;
using IceRpc.Transports;
using System.Net.Security;

IDispatcher dispatcher = ...;

await using var server = new Server(
    dispatcher,
    new SslServerAuthenticationOptions { ... },
    multiplexedServerTransport: new QuicServerTransport());

server.Listen();
```

## Platform requirements

IceRpc.Quic has the same platform requirements as `System.Net.Quic`. Microsoft documents
these requirements as the [HTTP/3 platform dependencies][platform].


[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Transports.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/GreeterQuic
[icerpc]: https://www.nuget.org/packages/IceRpc
[quic]: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview
[package]: https://www.nuget.org/packages/IceRpc.Quic
[platform]: https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-http3#platform-dependencies
[product]: https://docs.testing.zeroc.com/docs/icerpc-core
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Quic
