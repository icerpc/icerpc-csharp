// Copyright (c) ZeroC, Inc.

using System.Reflection;

namespace IceRpc.BuildTelemetry.Reporter;

public partial record struct SliceTelemetry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SliceTelemetry" /> struct using the specified command-line arguments.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    public SliceTelemetry(string[] args)
    {
        // Determine the IceRPC version using the assembly version
        var assembly = Assembly.GetAssembly(typeof(SliceTelemetry));
        string version = assembly?.GetName()?.Version?.ToString() ?? "unknown";

        // Parse the compilation hash
        string compilationHash = args
            .SkipWhile(arg => arg != "--hash")
            .Skip(1)
            .FirstOrDefault() ?? "unknown";

        // Parse the compilation hash
        bool containsSlice1 = bool.TryParse(args
            .SkipWhile(arg => arg != "--contains-slice1")
            .Skip(1)
            .FirstOrDefault(), out var result) && result;

        IceRpcVersion = version;
        Source = new Source.CSharp(Environment.Version.ToString());
        OperatingSystem = Environment.OSVersion.ToString();
        CompilationHash = compilationHash;
        ContainsSlice1 = containsSlice1;
    }
}

public partial record struct ProtobufTelemetry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProtobufTelemetry" /> struct using the specified command-line arguments.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    public ProtobufTelemetry(string[] args)
    {
        // Determine the IceRPC version using the assembly version
        var assembly = Assembly.GetAssembly(typeof(ProtobufTelemetry));
        string version = assembly?.GetName()?.Version?.ToString() ?? "unknown";

        // Parse the compilation hash
        string compilationHash = args
            .SkipWhile(arg => arg != "--hash")
            .Skip(1)
            .FirstOrDefault() ?? "unknown";

        IceRpcVersion = version;
        Source = new Source.CSharp(Environment.Version.ToString());
        OperatingSystem = Environment.OSVersion.ToString();
        CompilationHash = compilationHash;
    }
}
