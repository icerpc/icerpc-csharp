// Copyright (c) ZeroC, Inc.

namespace IceRpc.BuildTelemetry;

public partial record struct SliceTelemetryData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SliceTelemetryData" /> struct using the version, compilation hash, and
    /// whether the Slice compilation contains Slice1 files.
    /// </summary>
    /// <param name="version">The version of the IceRPC.</param>
    /// <param name="compilationHash">The SHA-256 hash of the Slice files.</param>
    /// <param name="containsSlice1">Whether the Slice compilation contains Slice1 files.</param>
    /// <param name="containsSlice2">Whether the Slice compilation contains Slice2 files.</param>
    /// <param name="sourceFileCount">The number of source files in the Slice compilation.</param>
    /// <param name="referenceFileCount">The number of reference files in the Slice compilation.</param>
    public SliceTelemetryData(string version, string compilationHash, bool containsSlice1, bool containsSlice2, int sourceFileCount, int referenceFileCount)
    {
        // The source of the build telemetry is Slice and the version is the current runtime version.
        ToolVersion = version;
        TargetLanguage = new TargetLanguage.CSharp(Environment.Version.ToString());
        OperatingSystem = SystemHelpers.GetOperatingSystem();
        ContainsSlice1 = containsSlice1;
        ContainsSlice2 = containsSlice2;
        SourceFileCount = sourceFileCount;
        ReferenceFileCount = referenceFileCount;
        Architecture = SystemHelpers.GetArchitecture();
        IsCI = SystemHelpers.IsCi();
        CompilationHash = compilationHash;
    }
}
