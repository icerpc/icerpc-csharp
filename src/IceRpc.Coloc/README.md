# Coloc Transport for IceRPC

IceRpc.Coloc is an implementation of [IceRPC][icerpc]'s duplex transport abstraction. You can use this transport to send
RPCs to services hosted by IceRPC servers in your local application.

The primary use-case for this transport is testing. Several unit tests running in the same process can easily and cheaply
create their own private coloc connections.

[Source code][source] | [Package][package] | [API reference documentation][api] | [Product documentation][product]

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Transports.html
[icerpc]: https://www.nuget.org/packages/IceRpc
[package]: https://www.nuget.org/packages/IceRpc.Coloc
[product]: https://docs.testing.zeroc.com/docs/icerpc-core
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Coloc
