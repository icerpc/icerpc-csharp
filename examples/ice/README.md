# Examples

This folder contains example applications that showcase the IceRPC + Ice integration. All these examples use the
`ice` protocol since this integration is provided primarily for interop with [Ice].

|                       |                                                                                                  |
|-----------------------|--------------------------------------------------------------------------------------------------|
| [Greeter](./Greeter/) | Shows how an IceRPC client can call a service hosted by an [Ice] server, and vice versa.         |
| [IceGrid](./IceGrid/) | Shows how an IceRPC client can call services hosted by servers managed by the [IceGrid service]. |
| [Ordered](./Ordered/) | Shows how to dispatch the requests received over a connection one at a time and in order.        |

[Ice]: https://zeroc.com/products/ice
[IceGrid service]: https://zeroc.com/products/ice/services/icegrid
