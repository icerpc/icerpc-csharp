// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Runtime.CompilerServices;

// Make internals visible to coloc and interop assembly
[assembly: InternalsVisibleTo("IceRpc.Coloc")] // necessary to use IceRpc.Transports.Internal.AsyncQueue
[assembly: InternalsVisibleTo("IceRpc.Interop")]

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
[assembly: InternalsVisibleTo("IceRpc.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Interop.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Conformance.Tests")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Common")]
