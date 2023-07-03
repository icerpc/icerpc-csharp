// Copyright (c) ZeroC, Inc.

using System.Runtime.CompilerServices;

// Make internals visible to coloc assembly
[assembly: InternalsVisibleTo("IceRpc.Transports.Coloc")] // necessary to use IceRpc.Transports.Internal utility classes
[assembly: InternalsVisibleTo("IceRpc.Transports.Quic")] // necessary to use ToIceRpcException SocketException extension

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
[assembly: InternalsVisibleTo("IceRpc.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Conformance.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Common")]

[assembly: InternalsVisibleTo("IceRpc.Compressor.Tests")] // For GetPayloadWriter
[assembly: InternalsVisibleTo("IceRpc.Telemetry.Tests")] // For SliceEncoder internal constructor
