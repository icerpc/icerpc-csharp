# IceRPC Slice files

This directory provides [Slice] definitions shared by all IceRPC implementations.

| Subdirectory       | Description                                                                        |
|--------------------|------------------------------------------------------------------------------------|
| Ice                | Interfaces and exceptions provided for interop with [Ice] applications. |
| IceRpc             | Types provided by all IceRPC implementations. These types are IceRPC-specific.     |
| IceRpc/**/Internal | Types used by IceRPC implementations to implement the [ice][ice-protocol] protocol, the [icerpc][icerpc-protocol] protocol, the [Slic] protocol and more. These are internal IceRPC implementation details—applications built with IceRPC don't need to see these definitions.|
| WellKnownTypes     | Custom types such as Uri and TimeStamp. These well-known types are RPC-independent.|

The copy of record for these Slice files is the [icerpc-slice] repository. Each IceRPC implementation is
expected to create its own read-only clone of these Slice files with `git subtree`. For example, the `slice` directory
of the [icerpc-csharp] repository is a git subtree clone of icerpc-slice.

[Ice]: https://github.com/zeroc-ice/ice
[ice-protocol]: https://docs.testing.zeroc.com/icerpc/ice-protocol/protocol-frames
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp/
[icerpc-protocol]: https://docs.testing.zeroc.com/icerpc/icerpc-protocol/mapping-rpcs-to-streams
[icerpc-slice]: https://github.com/icerpc/icerpc-slice
[Slic]: https://docs.testing.zeroc.com/icerpc/slic-transport/protocol-frames
[Slice]: https://docs.testing.zeroc.com/slice2
