# IceRPC for C# Examples

This directory contains example applications for various IceRPC components. These examples are provided to get you
started on using a particular IceRPC feature or programming technique.

## [Bidir](./Bidir/)

The bidir example application shows how to make bidirectional calls, that is the server callback a client using a
connection previously established by the client.

## [Compress](./Compress/)

The compress example application shows how to use the deflate interceptor and middleware to compress and decompres
the arguments and return value of an invocation.

## [GenericHost](./GenericHost/)

The generic host example application shows how to create IceRPC client and server applications using the .NET
dependency injection (DI).

## [Hello](./Hello/)

The hello example application shows how to create a minimal IceRPC client and server application, implementing the
canonical "Hello World" example.

## [Interop](./Interop/)

The interop example application shows how IceRPC inteoperates with ZeroC Ice using ice protocol and Slice 1 encoding.

## [OpenTelemetry](./OpenTelemetry/)

The OpenTelemetry example application shows how to use the telemetry interceptor and middleware, and how they can be
integrated with [OpenTelemetry](https://opentelemetry.io/) to export traces to [Zipkin](https://zipkin.io/).

## [RequestContext](./RequestContext/)

The request context example application shows how to attach contextual information to the invocation and how to access
this information from the dispatch.

## [Retry](./Retry/)

The Retry example application shows how to use the retry interceptor to retry failed invocations.

## [Secure](./Secure/)

The secure example application shows how to create a client and server application that communicates  using TLS secure
connections.

## [Stream](./Stream/)

The Stream example application shows how to stream data from a client to a server.
