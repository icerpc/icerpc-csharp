// Copyright (c) ZeroC, Inc.

using System.Runtime.InteropServices;

namespace IceRpc.BuildTelemetry;

/// <summary>
///  Helper class to get information about the operating system.
/// </summary>
internal static class SystemHelpers
{
    /// <summary> Get the operating system name in a human-readable format.</summary>
    /// <returns> The operating system name.</returns>
    internal static string GetOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }
        else
        {
            return "Unknown OS";
        }
    }

    /// <summary> Get the architecture of the current process.</summary>
    /// <returns> The architecture of the current process.</returns>
    internal static string GetArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    internal static bool IsCi()
    {
        // If the CI environment variable is set, we are running in a CI environment.
        return Environment.GetEnvironmentVariable("CI") != null;
    }
}
