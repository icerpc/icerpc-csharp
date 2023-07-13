// Copyright (c) ZeroC, Inc.

using System.Runtime.CompilerServices;

// Make internals visible to IceRpc.Slice
[assembly: InternalsVisibleTo("IceRpc.Slice")] // necessary to use InvalidPipeReader and EmptyPipeReader
// Make internals visible to coloc assembly
[assembly: InternalsVisibleTo("IceRpc.Transports.Coloc")] // necessary to use IceRpc.Transports.Internal utility classes

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
[assembly: InternalsVisibleTo("IceRpc.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Conformance.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Common")]

[assembly: InternalsVisibleTo("IceRpc.Compressor.Tests")] // For GetPayloadWriter
