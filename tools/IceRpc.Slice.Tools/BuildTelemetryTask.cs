// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace IceRpc.Slice.Tools;

/// <summary>
/// A MSBuild task that reports Slice build telemetry data to the IceRPC build telemetry service.
/// </summary>
public class BuildTelemetryTask : ToolTask
{
    /// <summary>
    /// Gets or sets the compilation hash.
    /// </summary>
    [Required]
    public string CompilationHash { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether the compilation contained any Slice1 files.
    /// </summary>
    public bool ContainsSlice1 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the compilation contained any Slice2 files.
    /// </summary>
    public bool ContainsSlice2 { get; set; }

    /// <summary>
    /// Gets or sets the number of source files in the Slice compilation.
    /// </summary>
    public int SourceFileCount { get; set; }

    /// <summary>
    /// Gets or sets the number of reference files in the Slice compilation.
    /// </summary>
    public int ReferenceFileCount { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    [Required]
    public string WorkingDirectory { get; set; } = "";

    /// <inheritdoc/>
    protected override string ToolName => "dotnet";

    /// <inheritdoc/>
    protected override string GenerateCommandLineCommands()
    {
        var commandLine = new CommandLineBuilder();
        commandLine.AppendFileNameIfNotNull("IceRpc.BuildTelemetry.Reporter.dll");
        commandLine.AppendSwitch("--hash");
        commandLine.AppendSwitch(CompilationHash);
        commandLine.AppendSwitch("--idl");
        commandLine.AppendSwitch("slice");

        commandLine.AppendSwitch("--contains-slice1");
        commandLine.AppendSwitch(ContainsSlice1.ToString());
        commandLine.AppendSwitch("--contains-slice2");
        commandLine.AppendSwitch(ContainsSlice2.ToString());
        commandLine.AppendSwitch("--src-file-count");
        commandLine.AppendSwitch(SourceFileCount.ToString());
        commandLine.AppendSwitch("--ref-file-count");
        commandLine.AppendSwitch(ReferenceFileCount.ToString());

        return commandLine.ToString();
    }

    /// <inheritdoc/>
    protected override string GenerateFullPathToTool() => ToolName;

    /// <summary>
    /// Overriding this method to suppress any warnings or errors.
    /// </summary>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance) { }
}
