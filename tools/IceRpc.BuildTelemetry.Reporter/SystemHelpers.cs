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
    internal static string GetArchitecture() => RuntimeInformation.ProcessArchitecture.ToString();

    internal static bool IsCi()
    {
        // CI environment variables as listed in the .csproj file
        string[] ciEnvironmentVariables = new string[]
        {
        "TF_BUILD", // Azure Pipelines / DevOpsServer
        "GITHUB_ACTIONS", // GitHub Actions
        "APPVEYOR", // AppVeyor
        "CI", // General, set by many build agents
        "TRAVIS", // Travis CI
        "CIRCLECI", // Circle CI
        "CODEBUILD_BUILD_ID", // AWS CodeBuild
        "AWS_REGION", // AWS CodeBuild region
        "BUILD_ID", // Jenkins, Google Cloud Build
        "BUILD_URL", // Jenkins
        "PROJECT_ID", // Google Cloud Build
        "TEAMCITY_VERSION", // TeamCity
        "JB_SPACE_API_URL" // JetBrains Space
        };

        // Check if any of the CI environment variables are set
        foreach (var envVar in ciEnvironmentVariables)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVar)))
            {
                return true;
            }
        }

        return false;
    }
}
