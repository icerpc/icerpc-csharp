# Examples

This folder contains example applications that showcase the IceRPC + Protobuf integration.

|                                        |                                                                                                                                     |
|----------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------|
| [Deadline](./Deadline/)                | Shows how to use the deadline interceptor to set the invocation deadline.                                                           |
| [GenericHost](./GenericHost/)          | Shows how to create client and server applications using Microsoft's Dependency Injection container.                                |
| [Greeter](./Greeter/)                  | Shows how to call and implement a canonical Greeter service using the IceRPC + Protobuf integration.                                |
| [Logger](./Loggger/)                   | Shows how to enable logging.                                                                                                        |
| [Metrics](./Metrics/)                  | Shows how to use the metrics interceptor and middleware.                                                                            |
| [MultipleServices](./MultipleServices) | Shows how a service can implement multiple Protobuf services.                                                                       |
| [Quic](./Quic/)                        | Shows how to use the QUIC transport.                                                                                                |
| [RequestContext](./RequestContext/)    | Shows how to attach information to an invocation and retrieve this information from the dispatch in the server.                     |
| [Retry](./Retry/)                      | Shows how to use the retry interceptor to retry failed requests.                                                                    |
| [Secure](./Secure/)                    | Shows how to secure TCP connections with TLS.                                                                                       |
| [Stream](./Stream/)                    | Shows how to stream data from a client to a server.                                                                                 |
| [TcpFallback](./TcpFallback/)          | Shows how to create client and server applications that communicate over QUIC when possible but can fall back to TCP.               |
| [Telemetry](./Telemetry/)              | Shows how to use the telemetry interceptor and middleware.                                                                          |
| [Thermostat](./Thermostat/)            | Shows how to send requests via an intermediary server; includes sending requests the "other way around", from a server to a client. |
