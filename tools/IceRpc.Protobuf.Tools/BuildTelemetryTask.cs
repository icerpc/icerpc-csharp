// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace IceRpc.Protobuf.Tools;

/// <summary>
/// A MSBuild task that reports Protobuf build telemetry data to the IceRPC build telemetry service.
/// </summary>
public class BuildTelemetryTask : ToolTask
{
    /// <summary>
    /// Gets or sets the compilation hash.
    /// </summary>
    [Required]
    public string CompilationHash { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of source files in the Slice compilation.
    /// </summary>
    public int SourceFileCount { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    [Required]
    public string WorkingDirectory { get; set; } = "";

    /// <inheritdoc/>
    protected override string ToolName => "dotnet";

    /// <inheritdoc/>
    protected override string GetWorkingDirectory() => WorkingDirectory;

    /// <inheritdoc/>
    protected override string GenerateFullPathToTool() => ToolName;

    /// <inheritdoc/>
    protected override string GenerateCommandLineCommands()
    {
        var commandLine = new CommandLineBuilder();
        commandLine.AppendFileNameIfNotNull("IceRpc.BuildTelemetry.Reporter.dll");
        commandLine.AppendSwitch("--hash");
        commandLine.AppendSwitch(CompilationHash);
        commandLine.AppendSwitch("--idl");
        commandLine.AppendSwitch("protobuf");
        commandLine.AppendSwitch("--src-file-count");
        commandLine.AppendSwitch(SourceFileCount.ToString());
        return commandLine.ToString();
    }

    /// <summary>
    /// Overriding this method to suppress any warnings or errors.
    /// </summary>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance) { }
}
