// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace IceRpc.Telemetry.Internal;

public partial record struct Telemetry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Telemetry" /> struct using the specified command-line arguments.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    public Telemetry(string[] args)
    {
        // Parse command-line arguments to get the version
        string version = args
            .SkipWhile(arg => arg != "--version")
            .Skip(1)
            .FirstOrDefault() ?? "unknown";

        // Parse command-line arguments to get the source
        string source = args
            .SkipWhile(arg => arg != "--source")
            .Skip(1)
            .FirstOrDefault() ?? "unknown";

        // Parse command-line arguments to get the hash
        string hash = args
            .SkipWhile(arg => arg != "--hash")
            .Skip(1)
            .FirstOrDefault() ?? "unknown";

        // Parse command-line arguments to get the updated-files
        bool? updatedFiles = args
            .SkipWhile(arg => arg != "--updated-files")
            .Skip(1)
            .Select(arg => bool.TryParse(arg, out bool result) ? (bool?)result : null)
            .FirstOrDefault();

        IceRpcVersion = version;
        Source = source;
        OperatingSystem = Environment.OSVersion.ToString();
        ProcessorCount = Environment.ProcessorCount;
        Memory = Process.GetCurrentProcess().Threads.Count;
        Hash = hash;
        UpdatedFiles = updatedFiles;
    }
}
