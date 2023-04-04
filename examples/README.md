# IceRPC for C# Examples

This folder contains example applications for various IceRPC components. These examples have been provided to help get
you started using a particular IceRPC feature or programming technique.

- [Building Example Programs](#building-example-programs)
- [Example Programs](#example-programs)
  * [Authorization](#authorization)
  * [Compress](#compress)
  * [Download](#download)
  * [GenericHost](#generichost)
  * [Greeter](#greeter)
  * [GreeterCore](#greetercore)
  * [GreeterQuic](#greeterquic)
  * [GreeterLog](#greeterlog)
  * [Metrics](#metrics)
  * [OpenTelemetry](#opentelemetry)
  * [RequestContext](#requestcontext)
  * [Retry](#retry)
  * [Secure](#secure)
  * [Stream](#stream)
  * [Upload](#upload)

## Building Example Programs

You can build each demo by using `dotnet build` command and the corresponding solution or project files, the example
programs are configured to use IceRPC NuGet packages.

If you want to build all examples at once run the following command:

For Linux and macOS

```shell
./build.sh --examples
```

For Windows

```shell
build --examples
```

If you want to use the IceRPC distribution from this repository instead of IceRPC from a published NuGet package, you
will need to build and install the NuGet package from this repository before building the examples. This can be done by
running the following command:

For Linux and macOS

```shell
./build.sh --examples --srcdist
```

For Windows

```shell
build --examples --srcdist
```

## Example Programs

### [Authorization](./Authorization/)

The Authorization example application illustrates how to create an authorization interceptor and middleware that can
be used to authorize requests.

### [Compress](./Compress/)

The Compress example shows how to use the deflate interceptor and middleware to compress and decompress the arguments
and return the value of an invocation.

### [Download](./Download/)

The Download example shows how to download a file using streaming from a server.

### [GenericHost](./GenericHost/)

The GenericHost example shows how to create client and server applications using Microsoft's Dependency Injection
container.

### [Greeter](./Greeter/)

The Greeter example shows how to create a minimal client and server application implementing the canonical "Greeter World"
example.

### [GreeterCore](./GreeterCore/)

The GreeterCore example shows how to create a minimal client and server application using IceRPC's core API--without Slice
definitions or generated code.

### [GreeterQuic](./GreeterQuic/)

The GreeterQuic example is the Greeter example updated to use the QUIC transport instead of the default TCP transport.

### [GreeterLog](./GreeterLog/)

The GreeterLog example is the Greeter example updated to include logging.

### [Interop](./Interop/)

Contains example applications that shows how IceRPC interoperates with [ZeroC Ice][1].

### [Metrics](./Metrics/)

The Metrics example application shows how to use the metric interceptor and middleware with `dotnet-counters` and
`dotnet-trace` to monitor requests dispatched by a server.

### [OpenTelemetry](./OpenTelemetry/)

The OpenTelemetry example shows how to use the telemetry interceptor and middleware and how they can be integrated with
[OpenTelemetry](https://opentelemetry.io/) to export traces to [Zipkin][2].

### [RequestContext](./RequestContext/)

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

[1]: https://github.com/zeroc-ice/ice
[2]: https://zipkin.io/
