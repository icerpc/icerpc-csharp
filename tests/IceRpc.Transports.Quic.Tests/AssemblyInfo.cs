// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Runtime.Versioning;

// We need these attributes because the .NET QUIC APIs have the same.
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macos")]
[assembly: SupportedOSPlatform("windows")]

// On Linux, two QUIC listeners bound to port 0 at the same time can be assigned the same UDP port, and one of them
// then misses the connections sent to that port. Linux 7.4 fixes this kernel bug.
[assembly: NonParallelizable]
