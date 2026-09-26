// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Tests.Common;

/// <summary>Provides a helper for tests that rely on the TCP listen backlog, for example tests that fill the backlog
/// to make a connection establishment block.</summary>
public static class ListenBacklogSupport
{
    /// <summary>Ignores the current test when the OS does not honor the TCP listen backlog.</summary>
    /// <remarks>macOS 26.0 and macOS 27.0 do not honor the listen backlog: they accept a number of connections
    /// unrelated to the backlog, and then reset new connections instead of leaving them pending. macOS 26.1 fixed this
    /// bug and macOS 27.0 reintroduced it.</remarks>
    public static void IgnoreTestIfNotHonored()
    {
        if (OperatingSystem.IsMacOS() && Environment.OSVersion.Version is { Major: 26 or 27, Minor: 0 })
        {
            Assert.Ignore($"The listen backlog is not honored on macOS {Environment.OSVersion.Version}.");
        }
    }
}
