// Copyright (c) ZeroC, Inc.

using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
[assembly: InternalsVisibleTo("IceRpc.Quic.Tests")]

// We need these attributes because the .NET QUIC APIs have the same.
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macOS")]
[assembly: SupportedOSPlatform("windows")]
