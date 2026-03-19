// Copyright (c) ZeroC, Inc.

using System.Runtime.CompilerServices;

// Make internals visible to IceRpc.Ice for IceIdentity and more.
[assembly: InternalsVisibleTo("IceRpc.Ice")]

// Make internals visible to IceRpc.Slice for ReadSliceSegmentAsync and more.
[assembly: InternalsVisibleTo("IceRpc.Slice")]

// Make internals visible to coloc assembly
[assembly: InternalsVisibleTo("IceRpc.Transports.Coloc")] // necessary to use IceRpc.Transports.Internal utility classes

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
[assembly: InternalsVisibleTo("IceRpc.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Ice.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Ice.Generator.None.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Slice.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Slice.Generator.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Conformance.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Common")]
[assembly: InternalsVisibleTo("IceRpc.Quic.Tests")]

[assembly: InternalsVisibleTo("IceRpc.Compressor.Tests")] // For GetPayloadWriter
