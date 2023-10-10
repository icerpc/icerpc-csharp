// Copyright (c) ZeroC, Inc.

using System.Runtime.Versioning;

[assembly: NUnit.Framework.Timeout(8000)]
// TODO: Remove once OpenSSL performance issues are resolved (https://github.com/openssl/openssl/issues/17627).
[assembly: NUnit.Framework.LevelOfParallelism(2)]

// We need these attributes because the .NET QUIC APIs have the same.
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macOS")]
[assembly: SupportedOSPlatform("windows")]
