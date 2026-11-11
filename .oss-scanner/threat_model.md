# IceRPC for C#: threat model for the OSS Scanner

IceRPC is an RPC framework for .NET. It implements two RPC protocols:

- **icerpc**, its own protocol, over a multiplexed transport: QUIC (`System.Net.Quic` with msquic), or Slic, IceRPC's
  own multiplexing layer, over TCP with or without TLS.
- **ice**, the protocol of ZeroC Ice, over TCP with or without TLS, for interoperability with Ice applications.

Both protocols carry request and response payloads as streams of bytes, in an encoding the application chooses.
IceRPC provides three payload encodings: Slice (`ZeroC.Slice.Codec` and `IceRpc.Slice`), Ice (`src/IceRpc/Ice/Codec`
and `IceRpc.Ice`), and Protobuf (`IceRpc.Protobuf`, with Google.Protobuf as the message parser).

The build generates C# code from the IDL files:

- `.slice` files: slicec runs `ZeroC.Slice.Generator`, which generates the types with their encoding and decoding
  code, and `IceRpc.Slice.Generator`, which generates the proxies and services with the encoding and decoding of
  operation arguments and return values.
- `.proto` files: protoc generates the messages with their serialization code, and runs `IceRpc.Protobuf.Generator`,
  which generates the clients and services.
- `.ice` files: slice2cs, the Slice compiler of ZeroC Ice 3.8, from the `ZeroC.Ice.Slice.Tools` package, generates
  the types, proxies and services.

`IceRpc.ServiceGenerator`, a C# source generator, implements the dispatch of service classes.

Interceptors and middleware (`IceRpc.Compressor`, `IceRpc.Deadline`, `IceRpc.Locator`, `IceRpc.RequestContext`,
`IceRpc.Retry`, `IceRpc.Telemetry`, ...) decorate invocations and dispatches. A few decode a field or decompress a
payload received from the peer.

`SECURITY.md` lists the supported versions. This scan targets `main`; fixes are backported to the supported release
branches.

## Where untrusted input enters

The attacker is a remote peer that can connect to an IceRPC server, or a server that an IceRPC client connects to:
responses, protocol frames and transport frames sent by a server are as untrusted as requests sent by a client. TLS
authenticates the peer only when the application requires and verifies a certificate, and a malicious peer can hold a
valid one.

IceRPC is layered: a transport carries the frames of a protocol, a protocol carries requests and responses, and the
application encodes their payloads independently of both. Each layer is an attack surface of its own; in order of
exposure:

- **The transports**: Slic framing, stream management and flow control in `src/IceRpc/Transports/Slic`; the TCP
  transport and the server's handling of new connections in `src/IceRpc/Transports/Tcp` and `src/IceRpc/Server.cs`;
  the QUIC transport's adaptation of `System.Net.Quic` streams in `src/IceRpc/Transports/Quic`. Slic encodes its
  frames with a small subset of the Slice encoding, and limits their size with `MaxStreamFrameSize` in
  `SlicTransportOptions`.
- **The protocols** treat payloads as opaque streams of bytes. `src/IceRpc/Internal/IceRpcProtocolConnection.cs`
  (`Settings` and `GoAway` control frames, request and response headers with their fields) encodes its frames with a
  small subset of the Slice encoding. `src/IceRpc/Internal/IceProtocolConnection.cs` (`Request`, `RequestBatch`,
  `Reply`, `ValidateConnection` and `CloseConnection` frames, each starting with a prologue) encodes its frames with
  a subset of the Ice encoding; `IceDuplexConnectionDecorator.cs` implements its idle timeout. Both track dispatches,
  invocations and streams across connection shutdown and disposal, where races between a peer's frames and local
  shutdown are the most likely to hide bugs. `ConnectionOptions` holds their limits: `MaxIceRpcHeaderSize` (16,383
  bytes), `MaxIceFrameSize` (1 MB), and `MaxDispatches` and the maximum numbers of icerpc streams, which bound
  concurrency per connection.
- **The payload encodings** are independent of the protocols: with icerpc or ice, the application encodes a payload
  with Slice, Ice, Protobuf, or an encoding of its own, such as JSON. Each decoder enforces its own budget,
  configured through a feature:
  - Slice: `ZeroC.Slice.Codec/SliceDecoder.cs` and `SliceDecoderExtensions.cs`, the stream decoding in
    `IceRpc.Slice/Operations/PipeReaderExtensions.cs`, and the code generated from `.slice` files. The budget is
    `MaxSegmentSize` (1 MB) and `MaxCollectionAllocation` (8 MB) in `ISliceFeature`.
  - Ice: `src/IceRpc/Ice/Codec` (classes and class graphs, sliced classes and exceptions, tagged values, proxies and
    the server addresses they carry) and the code generated from `.ice` files. The budget is `MaxPayloadSize` (1 MB),
    `MaxCollectionAllocation` (8 MB) and `MaxDepth` (100) in `IIceFeature`.
  - Protobuf: the length-prefixed message framing in `IceRpc.Protobuf/RpcMethods`. The budget is `MaxMessageLength`
    (1 MB) in `IProtobufFeature`.
- **Interceptors and middleware** are an attack surface only where they read input from the peer. The deadline,
  request context and telemetry middleware each decode a request field. The compressor interceptor and middleware
  (`IceRpc.Compressor`) decode the compression format field and decompress the payload. The locator interceptor
  (`IceRpc.Locator`) uses the proxies that a locator server returns.

## What is trusted

- **`.slice`, `.ice` and `.proto` files, the code generators and the build**: IDL files are written by the application
  developer and compiled at build time, like C# source. Generator crashes or uncompilable output on unusual IDL are
  ordinary bugs, not vulnerabilities. The binaries the build downloads (slicec, protoc, and NuGet packages, including
  slice2cs in `ZeroC.Ice.Slice.Tools`) come from trusted upstreams. The generated code is not trusted, however: it
  decodes peer data at run time.
- **The application's own code and configuration**: services, interceptors and middleware it installs, options,
  `SslServerAuthenticationOptions` and `SslClientAuthenticationOptions`, including how certificates are validated,
  the service addresses it gives to proxies, and dependency injection setup.
- **The Coloc transport** (`IceRpc.Transports.Coloc`), which only connects code within the same process.
- **Third-party components**: `SslStream`, `System.Net.Quic` and msquic, `System.IO.Compression` and Google.Protobuf.
  A vulnerability in them is out of scope, unless IceRPC uses them in a way that creates it.

## How to exercise it

The image contains the checkout at `/src`, built in the Debug configuration, as described at the top of
`/src/.oss-scanner/Dockerfile`. Every NuGet package the solution needs is in `~/.nuget/packages`, so modified or new
tests in the existing test projects build offline:

```shell
cd /src
dotnet test tests/IceRpc.Tests --filter "FullyQualifiedName~IceRpcProtocolConnectionTests"
```

New projects or new package references cannot be restored offline; add reproducers as NUnit tests to an existing test
project instead. Useful starting points:

- Raw frames sent to a protocol connection or a Slic connection over an in-memory duplex connection:
  `Invalid_request_refused` in `tests/IceRpc.Tests/IceRpcProtocolConnectionTests.cs`,
  `Receiving_a_frame_larger_than_max_ice_frame_size_aborts_the_connection` in
  `tests/IceRpc.Tests/IceProtocolConnectionTests.cs`, and `Reject_stream_frame_exceeding_max_size` in
  `tests/IceRpc.Tests/Transports/Slic/SlicTransportTests.cs`.
- Decoding of hand-crafted buffers: `tests/ZeroC.Slice.Codec.Tests` and `tests/IceRpc.Ice.Codec.Tests`.
- Generated code: `tests/IceRpc.Slice.Generator.Tests`, `tests/IceRpc.Ice.Generator.Tests` and
  `tests/IceRpc.Protobuf.Tests`.

Most test projects install a trace listener (`tests/IceRpc.Tests.Common/AssertTraceListener.cs`) that turns a failed
`Debug.Assert` into a test failure. The codec test projects, `tests/ZeroC.Slice.Codec.Tests` and
`tests/IceRpc.Ice.Codec.Tests`, do not: there, a failed `Debug.Assert` terminates the test host process.

## How we rate severity

IceRPC is managed code, so we rate by what a remote peer can do under the attacker model above:

- **Critical**: arbitrary code execution, where the peer runs code of its choosing and not an operation that a
  service exposes; reading or corrupting memory outside the decoded buffer; data from another connection or request
  sent to a peer, such as the stale content of a pooled buffer; a client that reaches a service or dispatches a
  request that the application's routing or middleware should have refused; a TLS connection established without
  the certificate validation the application configured.
- **High**: a crafted frame or payload that terminates the process (an unhandled exception, such as one that escapes
  a timer callback, or a stack overflow), or that makes a server stop accepting connections or dispatching requests
  for other clients.
- **Medium**: resource exhaustion that small input can cause and that the configured limits do not bound, such as an
  allocation sized by a count that is not checked against the bytes remaining or the decoder's budget; a connection,
  stream or dispatch slot that a peer can leak so that it is never released; a hang of `ShutdownAsync` or
  `DisposeAsync` that a peer can cause beyond the configured timeouts.
- **Low**: denial of service that needs a large amount of traffic or a limit the application disabled or left
  unset, such as opening many connections while `ServerOptions.MaxConnections` is unlimited (the default); problems
  confined to the connection or request the attacker sent; internal details of the application sent to a peer, such
  as a stack trace.

A peer that sends malformed input gets an exception or an error response (`InvalidDataException`,
`IceRpcException`, `DispatchException` with `StatusCode.InvalidData`), or its connection aborted; that is the expected
behavior, not a finding. A check whose absence only changes which exception or status code the peer gets is not a
finding. Neither is information that IceRPC writes to the application's own logs.

The image is a Debug build: a `Debug.Assert` failure shows that a check is missing, but the rating depends on what a
Release build, where asserts are compiled out, does without it.

## Already considered and not vulnerabilities

`/src/DESIGN-DECISIONS.md` records the decisions that look like defects but are deliberate, most of them from earlier
audit findings closed as not planned. Read it before reporting. The ones most relevant to a scan:

- `.slice`, `.ice` and `.proto` files are trusted build inputs, and the binaries the build downloads are not checked
  against pinned hashes.
- Unknown values (enumerators, fields, Slic parameters, compression formats) are ignored for forward compatibility.
- Limits are enforced systemically, by the decoder budgets and frame size limits, not with a guard at each call site.
- Compressed payloads are decompressed as streams, without a size cap in the decompressor: the decoder that reads the
  decompressed stream enforces the bounds.
- The host is assumed to be little-endian.
- Hardening that only matters after an API contract is already violated by the application or by a caller, for
  example concurrent calls on a `PipeReader`.

Also dismissed after triage:

- When a service throws an exception other than `DispatchException`, the error message sent to the peer includes the
  exception's type and message, but not its stack trace. Applications are expected not to let arbitrary exceptions
  escape their services.
- With the ice protocol, a peer that sends an unsupported frame after the connection sent `CloseConnection` and has no
  pending dispatch or invocation gets its connection closed as if the shutdown completed normally.

## Reports and patches

- Name the component (transport, protocol, encoding, or library) in the title, and say whether the bug also applies to
  the supported release branches listed in `SECURITY.md`.
- The Slice and Ice codecs, the icerpc and ice protocol connections, and the Slice and Protobuf operation code share
  much of their structure. When a bug is in one, check the others and report each affected one in the same report.
- Patches must keep the wire format unchanged. Prefer patches that keep the public API unchanged, so that they can be
  backported to the release branches.
