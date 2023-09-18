---
layout: landing
---

# IceRPC for C# API Reference

Welcome to the IceRPC for C# API reference.

| Name                                    | Description                                                                                                                                        |
|-----------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------|
| [IceRpc]                                | The core of the IceRPC framework and various extensions for this core.                                                                             |
| [IceRpc.Compressor]                     | Provides the compressor interceptor and middleware, for compressing and decompressing the payloads of requests and responses.                      |
| [IceRpc.Deadline]                       | Provides the deadline interceptor and middleware.                                                                                                  |
| [IceRpc.Extensions.DependencyInjection] | Provides APIs related to Dependency Injection.                                                                                                     |
| [IceRpc.Features]                       | Provides the feature collection API and the core request features.                                                                                 |
| [IceRpc.Locator]                        | Provides the locator interceptor. This interceptor enables interop with Ice servers registered with a locator such as IceGrid.                     |
| [IceRpc.Logger]                         | Provides the logger interceptor and middleware, for logging requests and responses to an ILogger.                                                  |
| [IceRpc.Metrics]                        | Provides the metrics interceptor and middleware.                                                                                                   |
| [IceRpc.RequestContext]                 | Provides the request context interceptor and middleware.                                                                                           |
| [IceRpc.Retry]                          | Provides the retry interceptor.                                                                                                                    |
| [IceRpc.Slice]                          | Provides support for the IceRPC + Slice integration. The Slice compiler for C# generates code for Slice interfaces that relies on these APIs.      |
| [IceRpc.Slice.Ice]                      | Provides Ice-specific APIs, for interop with Ice applications.                                                                                     |
| [IceRpc.Telemetry]                      | Provides the telemetry interceptor and middleware; they add [OpenTelemetry] support to IceRPC.                                                     |
| [IceRpc.Transports]                     | Provides the duplex and multiplexed transport abstractions.                                                                                        |
| [IceRpc.Transports.Coloc]               | Provides the coloc duplex transport. It implements the duplex transport abstractions for "colocated" communications within the same address space. |
| [IceRpc.Transports.Quic]                | Provides the QUIC multiplexed transport. It implements the multiplexed transport abstractions using QUIC.                                          |
| [IceRpc.Transports.Slic]                | Provides the Slic multiplexing adapter. Slic implements the multiplexed transport abstractions over the duplex transport abstractions.             |
| [IceRpc.Transports.Tcp]                 | Provides the TCP duplex transport. It implements the duplex transport abstractions using plain TCP and TCP + SSL.                                  |
| [ZeroC.Slice]                           | Supports encoding/decoding structured data to/from bytes in the Slice format. The Slice compiler for C# generates code that relies on these APIs.  |

[IceRpc]: https://docs.icerpc.dev/api/csharp/api/IceRpc.html
[IceRpc.Compressor]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Compressor.html
[IceRpc.Deadline]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Deadline.html
[IceRpc.Extensions.DependencyInjection]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Extensions.DependencyInjection.html
[IceRpc.Features]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Features.html
[IceRpc.Locator]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Locator.html
[IceRpc.Logger]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Logger.html
[IceRpc.Metrics]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Metrics.html
[IceRpc.RequestContext]: https://docs.icerpc.dev/api/csharp/api/IceRpc.RequestContext.html
[IceRpc.Retry]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Retry.html
[IceRpc.Slice]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Slice.html
[IceRpc.Slice.Ice]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Slice.Ice.html
[IceRpc.Telemetry]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Telemetry.html
[IceRpc.Transports]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Transports.html
[IceRpc.Transports.Coloc]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Transports.Coloc.html
[IceRpc.Transports.Quic]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Transports.Quic.html
[IceRpc.Transports.Slic]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Transports.Slic.html
[IceRpc.Transports.Tcp]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Transports.Tcp.html
[ZeroC.Slice]: https://docs.icerpc.dev/api/csharp/api/ZeroC.Slice.html
