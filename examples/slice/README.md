# Examples

This folder contains example applications that showcase the IceRPC + Slice integration.

|                                            |                                                                                                                                     |
|--------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------|
| [Compress](./Compress/)                    | Shows how to use the compress interceptor and middleware.                                                                           |
| [CustomError](./CustomError/)              | Shows how to return custom errors from operations with the Result<Success, Failure> type.                                           |
| [Deadline](./Deadline/)                    | Shows how to use the deadline interceptor to set the invocation deadline.                                                           |
| [DiscriminatedUnion](./DiscriminatedUnion/)| Shows how to define and use discriminated unions in Slice.                                                                          |
| [Download](./Download/)                    | Shows how to download a file from a server by streaming this file.                                                                  |
| [GenericHost](./GenericHost/)              | Shows how to create client and server applications using Microsoft's Dependency Injection container.                                |
| [Greeter](./Greeter/)                      | Shows how to call and implement a canonical Greeter service using the IceRPC + Slice integration.                                   |
| [InteropIceGrid](./InteropIceGrid/)        | Shows how an IceRPC client can call services hosted by servers managed by [IceGrid].                                                |
| [InteropMinimal](./InteropMinimal/)        | shows how an IceRPC client can call a service hosted by an [Ice] server.                                                            |
| [Logger](./Loggger/)                       | Shows how to enable logging.                                                                                                        |
| [Metrics](./Metrics/)                      | Shows how to use the metrics interceptor and middleware.                                                                            |
| [MultipleInterfaces](./MultipleInterfaces) | Shows how a service can implement multiple interfaces.                                                                              |
| [Quic](./Quic/)                            | Shows how to use the QUIC transport.                                                                                                |
| [RequestContext](./RequestContext/)        | Shows how to attach information to an invocation and retrieve this information from the dispatch in the server.                     |
| [Retry](./Retry/)                          | Shows how to use the retry interceptor to retry failed requests.                                                                    |
| [Secure](./Secure/)                        | Shows how to secure TCP connections with TLS.                                                                                       |
| [Stream](./Stream/)                        | Shows how to stream data from a client to a server.                                                                                 |
| [TcpFallback](./TcpFallback/)              | Shows how to create client and server applications that communicate over QUIC when possible but can fall back to TCP.               |
| [Telemetry](./Telemetry/)                  | Shows how to use the telemetry interceptor and middleware.                                                                          |
| [Thermostat](./Thermostat/)                | Shows how to send requests via an intermediary server; includes sending requests the "other way around", from a server to a client. |
| [Upload](./Upload/)                        | Shows how to upload a file from a client to a server by streaming this file.                                                        |

[Ice]: https://zeroc.com/products/ice
[IceGrid]: https://zeroc.com/products/ice/services/icegrid
