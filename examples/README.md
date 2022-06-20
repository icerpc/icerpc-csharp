# IceRPC for C# Examples

This directory contains example applications for various IceRPC components. These examples have been provided to help
get you started using a particular IceRPC feature or programming technique.

## [Bidir](./Bidir/)

The Bidir example shows how to make bidirectional calls. A bidirectional call is a call in which the server calls back to
the client using a connection previously established by the client.

## [Compress](./Compress/)

The Compress example shows how to use the deflate interceptor and middleware to compress and decompress the arguments
and return the value of an invocation.

## [Download](./Download/)

The Download example shows how to download a file using streaming from a server.

## [GenericHost](./GenericHost/)

The GenericHost example shows how to create client and server applications using Microsoft's Dependency Injection
container.

## [Hello](./Hello/)

The Hello example shows how to create a minimal client and server application implementing the canonical "Hello World"
example.

## [Interop](./Interop/)

The Interop example application shows how IceRPC interoperates with [ZeroC Ice](https://github.com/zeroc-ice/ice).

## [OpenTelemetry](./OpenTelemetry/)

The OpenTelemetry example shows how to use the telemetry interceptor and middleware and how they can be integrated with
[OpenTelemetry](https://opentelemetry.io/) to export traces to [Zipkin](https://zipkin.io/).

## [RequestContext](./RequestContext/)

The RequestContext example shows how to attach contextual information to the invocation and access this information from
the dispatch.

## [Retry](./Retry/)

The Retry example shows how to use the retry interceptor to retry failed invocations.

## [Secure](./Secure/)

The Secure example shows how to create client and server applications that communicate using TLS secured connections.

## [Stream](./Stream/)

The Stream example shows how to stream data from a client to a server.

## [Upload](./Upload/)

The Upload example shows how to upload a file from a client to a sever using streaming.
