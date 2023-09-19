---
layout: landing
---

# C# API Reference

| Namespace                               | Description                                                                                                                                        |
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

[IceRpc]: api/IceRpc.yml
[IceRpc.Compressor]: api/IceRpc.Compressor.yml
[IceRpc.Deadline]: api/IceRpc.Deadline.yml
[IceRpc.Extensions.DependencyInjection]: api/IceRpc.Extensions.DependencyInjection.yml
[IceRpc.Features]: api/IceRpc.Features.yml
[IceRpc.Locator]: api/IceRpc.Locator.yml
[IceRpc.Logger]: api/IceRpc.Logger.yml
[IceRpc.Metrics]: api/IceRpc.Metrics.yml
[IceRpc.RequestContext]: api/IceRpc.RequestContext.yml
[IceRpc.Retry]: api/IceRpc.Retry.yml
[IceRpc.Slice]: api/IceRpc.Slice.yml
[IceRpc.Slice.Ice]: api/IceRpc.Slice.Ice.yml
[IceRpc.Telemetry]: api/IceRpc.Telemetry.yml
[IceRpc.Transports]: api/IceRpc.Transports.yml
[IceRpc.Transports.Coloc]: api/IceRpc.Transports.Coloc.yml
[IceRpc.Transports.Quic]: api/IceRpc.Transports.Quic.yml
[IceRpc.Transports.Slic]: api/IceRpc.Transports.Slic.yml
[IceRpc.Transports.Tcp]: api/IceRpc.Transports.Tcp.yml
[OpenTelemetry]: https://opentelemetry.io
[ZeroC.Slice]: api/ZeroC.Slice.yml
