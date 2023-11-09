# IceRPC for C# Examples

This folder contains example applications for IceRPC. Each example is a simple client-server application that
demonstrates a specific feature or programming technique.

## Branch and NuGet packages

The project files for the examples use NuGet packages for IceRPC and Slice. The version of the referenced NuGet packages
is branch-specific:

| icerpc-csharp branch | Referenced NuGet package version | .NET version |
|----------------------|----------------------------------|--------------|
| 0.1.x                | 0.1.*                            | .NET 7.0     |
| main                 | Not yet published on nuget.org   | .NET 8.0     |

If you want to build the examples for a released version (such as 0.1.x), please checkout the corresponding release
branch. For example:

```shell
git checkout -b 0.1.x origin/0.1.x
```

Then, when you build an example, the build uses the NuGet packages published on nuget.org.

If you want to build the examples on the `main` branch, you first need to build and publish locally the latest version
of the NuGet packages, as described in [BUILDING].

## Examples

|                                       |                                                                                                                                     |
|---------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------|
| [Compress](./Compress/)               | Shows how to use the compress interceptor and middleware.                                                                           |
| [Deadline](./Deadline/)               | Shows how to use the deadline interceptor to set the invocation deadline.                                                           |
| [Download](./Download/)               | Shows how to download a file from a server by streaming this file.                                                                  |
| [GenericHost](./GenericHost/)         | Shows how to create client and server applications using Microsoft's Dependency Injection container.                                |
| [Greeter](./Greeter/)                 | Shows how to call and implement a canonical Greeter service.                                                                        |
| [GreeterCore](./GreeterCore/)         | The Greeter example updated to use IceRPC's core API—without Slice definitions or generated code.                                   |
| [GreeterJson](./GreeterJson/)         | The Greeter example updated to use JSON instead of Slice.                                                                           |
| [GreeterLog](./GreeterLog/)           | The Greeter example updated to include logging.                                                                                     |
| [GreeterProtobuf](./GreeterProtobuf/) | The Greeter example updated to use Protobuf instead of Slice.                                                                       |
| [GreeterQuic](./GreeterQuic/)         | The Greeter example updated to use the QUIC transport.                                                                              |
| [Interop](./Interop/)                 | Contains examples that shows how IceRPC interoperates with [ZeroC Ice].                                                             |
| [Metrics](./Metrics/)                 | Shows how to use the metrics interceptor and middleware.                                                                            |
| [RequestContext](./RequestContext/)   | Shows how to attach information to an invocation and retrieve this information from the dispatch in the server.                     |
| [Retry](./Retry/)                     | Shows how to use the retry interceptor to retry failed requests.                                                                    |
| [Secure](./Secure/)                   | Shows how to secure TCP connections with TLS.                                                                                       |
| [Stream](./Stream/)                   | Shows how to stream data from a client to a server.                                                                                 |
| [StreamProtobuf](./StreamProtobuf/)   | The Stream example updated to use Protobuf instead of Slice.                                                                        |
| [Telemetry](./Telemetry/)             | Shows how to use the telemetry interceptor and middleware.                                                                          |
| [Thermostat](./Thermostat/)           | Shows how to send requests via an intermediary server; includes sending requests the "other way around", from a server to a client. |
| [Upload](./Upload/)                   | Shows how to upload a file from a client to a server by streaming this file.                                                        |

[BUILDING]: ../BUILDING.md
[ZeroC Ice]: https://github.com/zeroc-ice/ice
