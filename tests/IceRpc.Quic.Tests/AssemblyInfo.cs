// Copyright (c) ZeroC, Inc.

using System.Runtime.Versioning;

// TODO: we don't use 8000 here because of https://github.com/zeroc-ice/icerpc-csharp/issues/2700
[assembly: NUnit.Framework.Timeout(16000)]

// We need these attributes because the .NET QUIC APIs have the same.
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macOS")]
[assembly: SupportedOSPlatform("windows")]
