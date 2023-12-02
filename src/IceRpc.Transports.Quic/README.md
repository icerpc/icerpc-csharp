# QUIC transport for IceRPC

IceRpc.Transports.Quic allows you to use QUIC with [IceRPC][icerpc-csharp]. It's a thin layer over
[`System.Net.Quic`][quic].

QUIC is a new UDP-based multiplexed transport used by HTTP/3 and several other application protocols.

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Product documentation][product]

## Sample code

```csharp
//  Create an IceRPC client connection with QUIC

using IceRpc;
using IceRpc.Transports.Quic;
using System.Net.Security;

await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    new SslClientAuthenticationOptions { ... },
    multiplexedClientTransport: new QuicClientTransport());
```

```csharp
// Create an IceRPC server with QUIC

using IceRpc;
using IceRpc.Transports.Quic;
using System.Net.Security;

IDispatcher dispatcher = ...;

await using var server = new Server(
    dispatcher,
    new SslServerAuthenticationOptions { ... },
    multiplexedServerTransport: new QuicServerTransport());

server.Listen();
```

## Platform requirements

IceRpc.Transports.Quic has the same platform requirements as `System.Net.Quic`. Microsoft documents these requirements
as the [HTTP/3 platform dependencies][platform].

[api]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Transports.Quic.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/0.2.x/examples/GreeterQuic
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[quic]: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview
[package]: https://www.nuget.org/packages/IceRpc.Transports.Quic
[platform]: https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-http3#platform-dependencies
[product]: https://docs.icerpc.dev/icerpc
[source]: https://github.com/icerpc/icerpc-csharp/tree/0.2.x/src/IceRpc.Transports.Quic
