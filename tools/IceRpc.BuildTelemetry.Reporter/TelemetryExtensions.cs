// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace IceRpc.BuildTelemetry.Reporter;

public partial record struct Telemetry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Telemetry" /> struct using the specified command-line arguments.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    public Telemetry(string[] args)
    {
        // // Parse the version
        // string version = args
        //     .SkipWhile(arg => arg != "--version")
        //     .Skip(1)
        //     .FirstOrDefault() ?? "unknown";

        var assembly = System.Reflection.Assembly.GetAssembly(typeof(Program));
        string version = assembly.GetName().Version.ToString();

        // Parse the source
        string source = args
            .SkipWhile(arg => arg != "--source")
            .Skip(1)
            .FirstOrDefault() ?? "unknown";

        // Parse the compilation hash
        string compilationHash = args
            .SkipWhile(arg => arg != "--hash")
            .Skip(1)
            .FirstOrDefault() ?? "unknown";

        IceRpcVersion = version;
        Source = source;
        OperatingSystem = Environment.OSVersion.ToString();
        CompilationHash = compilationHash;
    }
}
