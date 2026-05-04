// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace IceRpc.Protobuf.Tools;

// Properties should not return arrays, disabled as this is standard for MSBuild tasks.
#pragma warning disable CA1819

/// <summary>An MSBuild task that runs <c>protoc</c> with a configurable set of plug-ins.</summary>
public class ProtocTask : ToolTask
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
    /// an empty <c>Path</c> indicates a protoc built-in such as <c>csharp</c>.</summary>
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

        // Register and enable each plug-in.
        foreach (ITaskItem plugin in Plugins)
        {
            string name = plugin.ItemSpec;
            string pluginPath = plugin.GetMetadata("Path");

            if (!string.IsNullOrEmpty(pluginPath))
            {
                builder.AppendSwitch("--plugin");
                builder.AppendFileNameIfNotNull($"protoc-gen-{name}={pluginPath}");
            }

            builder.AppendSwitch($"--{name}_out");
            builder.AppendFileNameIfNotNull(OutputDir);
        }

        // Append each option as a single argv token. AppendSwitchIfNotNull with an empty switch name quotes
        // values containing whitespace so a path with spaces in --dependency_out=... stays one token.
        foreach (string option in AdditionalOptions)
        {
            builder.AppendSwitchIfNotNull(string.Empty, option);
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
            builder.AppendSwitch("-I");
            builder.AppendFileNameIfNotNull(path);
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

    /// <summary> Process the diagnostics emitted by the protoc compiler and log them with the MSBuild logger.
    /// </summary>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance)
    {
        Debug.Assert(singleLine is not null);
        int colonCount = singleLine.Count(c => c == ':');
        if (colonCount >= 3)
        {
            // protoc returned a diagnostic in the form "file:line:column: message", parse it and log it
            // For example: greeter.proto:9:1: Expected top-level statement (e.g. "message").
            string[] parts = singleLine.Split([':'], 4);
            string fileName = parts[0];
            int lineNumber = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int columnNumber = int.Parse(parts[2], CultureInfo.InvariantCulture);
            string errorMessage = parts[3];

            Log.LogError("", "", "", fileName, lineNumber, columnNumber, -1, -1, errorMessage);
        }
        else
        {
            Log.LogError(singleLine);
        }
    }

    /// <inheritdoc/>
    protected override void LogToolCommand(string message) => Log.LogMessage(MessageImportance.Normal, message);
}
