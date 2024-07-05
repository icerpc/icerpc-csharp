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
    public ProtobufTelemetryData(string version, string compilationHash)
    {
        // The source of the build telemetry is C# and the version is the current runtime version.
        IceRpcVersion = version;
        TargetLanguage = new TargetLanguage.CSharp(Environment.Version.ToString());
        OperatingSystem = Environment.OSVersion.ToString();
        CompilationHash = compilationHash;
    }
}
