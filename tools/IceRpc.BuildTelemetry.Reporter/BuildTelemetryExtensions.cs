// Copyright (c) ZeroC, Inc.

namespace IceRpc.BuildTelemetry.Reporter;

public partial record struct SliceTelemetry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SliceTelemetry" /> struct using the version, compilation hash, and
    /// whether the Slice compilation contains Slice1 files.
    /// </summary>
    /// <param name="version">The version of the IceRPC.</param>
    /// <param name="compilationHash">The SHA-256 hash of the Slice files.</param>
    /// <param name="containsSlice1">Whether the Slice compilation contains Slice1 files.</param>
    public SliceTelemetry(string version, string compilationHash, bool containsSlice1)
    {
        // The source of the build telemetry is C# and the version is the current runtime version.
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
    /// Initializes a new instance of the <see cref="ProtobufTelemetry" /> struct using the version and compilation
    /// hash.
    /// </summary>
    /// <param name="version">The version of the IceRPC.</param>
    /// <param name="compilationHash">The SHA-256 hash of the Slice files.</param>
    public ProtobufTelemetry(string version, string compilationHash)
    {
        // The source of the build telemetry is C# and the version is the current runtime version.
        IceRpcVersion = version;
        Source = new Source.CSharp(Environment.Version.ToString());
        OperatingSystem = Environment.OSVersion.ToString();
        CompilationHash = compilationHash;
    }
}
