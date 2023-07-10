// Copyright (c) ZeroC, Inc.

using System.Runtime.Versioning;

// TODO: we don't use a 8 s timeout to workaround connection establishment delays that occur with the
// Connect_fails_if_listener_is_disposed test. The https://github.com/dotnet/runtime/issues/77310 issue is possibly the
// cause of this delay. The issue is fixed with .NET 8.
#if NET8_0_OR_GREATER
[assembly: NUnit.Framework.Timeout(8000)]
#else
[assembly: NUnit.Framework.Timeout(16000)]
#endif

// TODO: Remove once OpenSSL performance issues are resolved (https://github.com/openssl/openssl/issues/17627).
[assembly: NUnit.Framework.LevelOfParallelism(2)]

// We need these attributes because the .NET QUIC APIs have the same.
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macOS")]
[assembly: SupportedOSPlatform("windows")]
