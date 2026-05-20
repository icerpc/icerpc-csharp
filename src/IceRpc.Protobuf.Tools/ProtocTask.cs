// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace IceRpc.Protobuf.Tools;

// Properties should not return arrays, disabled as this is standard for MSBuild tasks.
#pragma warning disable CA1819

/// <summary>An MSBuild task that runs <c>protoc</c> with a configurable set of plug-ins.</summary>
public partial class ProtocTask : ToolTask
{
    /// <summary>Gets or sets additional options to pass verbatim to <c>protoc</c>.</summary>
    public string[] AdditionalOptions { get; set; } = [];

    /// <summary>Gets or sets the output directory shared by all configured plug-ins; corresponds to the
    /// <c>--&lt;name&gt;_out=</c> option of the <c>protoc</c> compiler.</summary>
    [Required]
    public string OutputDir { get; set; } = "";

    /// <summary>Gets or sets the protoc plug-ins to run. Each item's <c>Identity</c> is the plug-in name (for example
    /// <c>csharp</c>, <c>icerpc-csharp</c>, <c>icerpc-build-telemetry</c>). The <c>Path</c> metadata, when not empty,
    /// is the full path to the plug-in script and is passed via <c>--plugin protoc-gen-&lt;name&gt;=&lt;path&gt;</c>;
    /// an empty <c>Path</c> indicates a protoc built-in such as <c>csharp</c>. The <c>Parameter</c> metadata, when not
    /// empty, is passed via <c>--&lt;name&gt;_opt=&lt;parameter&gt;</c>.</summary>
    [Required]
    public ITaskItem[] Plugins { get; set; } = [];

    /// <summary>Gets or sets the directories in which to search for imports, corresponds to <c>-I</c> protoc compiler
    /// option.</summary>
    public string[] SearchPath { get; set; } = [];

    /// <summary>Gets or sets the Protobuf source files to compile, these are the input files pass to the protoc
    /// compiler.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>Gets or sets the directory containing the protoc compiler.</summary>
    [Required]
    public string ToolsPath { get; set; } = "";

    /// <summary>Gets or sets the working directory for executing the protoc compiler from.</summary>
    [Required]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1721:Property names should not match get methods",
        Justification = "This is by design, see ToolTask.GetWorkingDirectory documentation.")]
    public string WorkingDirectory { get; set; } = "";

    /// <inheritdoc/>
    protected override string ToolName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "protoc.exe" : "protoc";

    /// <inheritdoc/>
    protected override string GenerateCommandLineCommands()
    {
        var builder = new CommandLineBuilder(false);

        // Emit diagnostics in protoc's MSVS format ("file(line) : error|warning in column=N: msg") so
        // LogEventsFromTextOutput can rewrite them into MSBuild's canonical format reliably, including for paths
        // that contain colons (e.g., Windows drive-letter paths).
        builder.AppendSwitch("--error_format=msvs");

        // Register and enable each plug-in.
        foreach (ITaskItem plugin in Plugins)
        {
            string name = plugin.ItemSpec;
            string pluginPath = plugin.GetMetadata("Path");

            if (!string.IsNullOrEmpty(pluginPath))
            {
                builder.AppendSwitchIfNotNull("--plugin=", $"protoc-gen-{name}={pluginPath}");
            }

            builder.AppendSwitchIfNotNull($"--{name}_out=", OutputDir);

            string pluginParameter = plugin.GetMetadata("Parameter");
            if (!string.IsNullOrEmpty(pluginParameter))
            {
                builder.AppendSwitchIfNotNull($"--{name}_opt=", pluginParameter);
            }
        }

        foreach (string option in AdditionalOptions)
        {
            builder.AppendTextUnquoted(" ");
            builder.AppendTextUnquoted(option);
        }

        var searchPath = new List<string>(SearchPath);

        // Add the sources directories to the import search path.
        foreach (ITaskItem source in Sources)
        {
            string fullPath = source.GetMetadata("FullPath");
            string? directory = Path.GetDirectoryName(fullPath);
            if (directory is not null && !searchPath.Contains(directory))
            {
                searchPath.Add(directory);
            }
        }

        foreach (string path in searchPath)
        {
            builder.AppendSwitchIfNotNull("-I", path);
        }
        builder.AppendFileNamesIfNotNull(Sources.Select(item => item.GetMetadata("FullPath")).ToArray(), " ");

        return builder.ToString();
    }

    /// <inheritdoc/>
    protected override string GenerateFullPathToTool()
    {
        string path = Path.Combine(ToolsPath, ToolName);
        if (!File.Exists(path))
        {
            Log.LogError($"Protoc compiler '{path}' not found.");
        }
        return path;
    }

    /// <inheritdoc/>
    protected override string GetWorkingDirectory() => WorkingDirectory;

    // Rewrites protoc's --error_format=msvs output ("file(line) : error|warning in column=N: msg") into MSBuild's
    // canonical diagnostic format ("file(line,N): error|warning: msg") so the base ToolTask logger can parse it via
    // CanonicalError. Non-matching lines pass through unchanged for the base class to handle.
    [GeneratedRegex(
        @"^(?<file>.+?)\((?<line>\d+)\)\s*:\s*(?<severity>error|warning) in column=(?<column>\d+):\s*(?<message>.*)$")]
    private static partial Regex DiagnosticRegex();

    /// <inheritdoc/>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance) =>
        base.LogEventsFromTextOutput(
            DiagnosticRegex().Replace(singleLine, "${file}(${line},${column}): ${severity}: ${message}"),
            messageImportance);

    /// <inheritdoc/>
    protected override void LogToolCommand(string message) => Log.LogMessage(MessageImportance.Normal, message);
}
