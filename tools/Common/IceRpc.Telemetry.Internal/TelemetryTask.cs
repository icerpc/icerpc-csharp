// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace IceRpc.Telemetry.Internal;

/// <summary>
/// A custom MSBuild task to run telemetry commands.
/// </summary>
public class TelemetryTask : ToolTask
{
    /// <summary>
    /// Gets or sets the version.
    /// </summary>
    [Required]
    public string Version { get; set; } = "";

    /// <summary>
    /// Gets or sets the hash.
    /// </summary>
    [Required]
    public string Hash { get; set; } = "";

    /// <summary>
    /// Gets or sets the updated files.
    /// </summary>
    [Required]
    public string UpdatedFiles { get; set; } = "";

    /// <summary>
    /// Gets or sets the source.
    /// </summary>
    [Required]
    public string Source { get; set; } = "";

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
        commandLine.AppendFileNameIfNotNull("IceRpc.Telemetry.Internal.dll");
        commandLine.AppendSwitch("--version");
        commandLine.AppendSwitch(Version);
        commandLine.AppendSwitch("--source");
        commandLine.AppendSwitch(Source);
        commandLine.AppendSwitch("--hash");
        commandLine.AppendSwitch(Hash);
        commandLine.AppendSwitch("--updated-files");
        commandLine.AppendSwitch(UpdatedFiles);

        return commandLine.ToString();
    }

    /// <summary>
    /// Overriding this method to suppress any warnings or errors.
    /// </summary>
    /// <inheritdoc/>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance) { }
}
