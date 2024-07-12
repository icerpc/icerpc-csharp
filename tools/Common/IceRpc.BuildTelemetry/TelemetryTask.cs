// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace IceRpc.BuildTelemetry;

/// <summary>
/// A custom MSBuild task to run telemetry commands.
/// </summary>
public class TelemetryTask : ToolTask
{
    /// <summary>
    /// Gets or sets the compilation hash.
    /// </summary>
    [Required]
    public string CompilationHash { get; set; } = "";

    /// <summary>
    /// Gets or sets the Idl.
    /// </summary>
    [Required]
    public string Idl { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether the compilation contained any Slice1 files.
    /// </summary>
    public bool ContainsSlice1 { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the compilation contained any Slice2 files.
    /// </summary>
    public bool ContainsSlice2 { get; set; } = false;

    /// <summary>
    /// Gets or sets the number of source files in the Slice compilation.
    /// </summary>
    public string? SourceFileCount { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of reference files in the Slice compilation.
    /// </summary>
    public string? ReferenceFileCount { get; set; } = "";

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
        commandLine.AppendSwitchIfNotNull("--hash", CompilationHash);
        commandLine.AppendSwitchIfNotNull("--idl", Idl);

        if (Idl == "Slice")
        {
            commandLine.AppendSwitchIfNotNull("--contains-slice1", ContainsSlice1.ToString());
            commandLine.AppendSwitchIfNotNull("--contains-slice2", ContainsSlice2.ToString());
            commandLine.AppendSwitchIfNotNull("--src-file-count", SourceFileCount);
            commandLine.AppendSwitchIfNotNull("--ref-file-count", ReferenceFileCount);
        }
        else if (Idl == "Protobuf")
        {
            commandLine.AppendSwitchIfNotNull("--src-file-count", SourceFileCount);
        }

        return commandLine.ToString();
    }

    /// <summary>
    /// Overriding this method to suppress any warnings or errors.
    /// </summary>
    /// <inheritdoc/>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance) { }
}
