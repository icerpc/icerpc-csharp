// Copyright (c) ZeroC, Inc.

namespace IceRpc.BuildTelemetry;

public partial record struct ProtobufTelemetryData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProtobufTelemetryData" /> struct using the version and compilation
    /// hash.
    /// </summary>
    /// <param name="version">The version of the IceRPC.</param>
    /// <param name="compilationHash">The SHA-256 hash of the Slice files.</param>
    /// <param name="fileCount">The number of source files in the Slice compilation.</param>
    public ProtobufTelemetryData(string version, string compilationHash, int fileCount)
    {
        // The source of the build telemetry is C# and the version is the current runtime version.
        ToolVersion = version;
        TargetLanguage = new TargetLanguage.CSharp(Environment.Version.ToString());
        OperatingSystem = SystemHelpers.GetOperatingSystem();
        Architecture = SystemHelpers.GetArchitecture();
        IsCI = SystemHelpers.IsCi();
        FileCount = fileCount;
        CompilationHash = compilationHash;
    }
}
