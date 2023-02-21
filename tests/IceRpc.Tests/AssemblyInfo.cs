// Copyright (c) ZeroC, Inc.

using System.Runtime.Versioning;

[assembly: NUnit.Framework.Timeout(8000)]

// We need these attributes because the .NET QUIC APIs have the same.
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macOS")]
[assembly: SupportedOSPlatform("windows")]
