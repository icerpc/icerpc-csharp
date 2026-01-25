// Copyright (c) ZeroC, Inc.

using IceRpc.CaseConverter.Internal;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace IceRpc.Protobuf.Tools;

// Properties should not return arrays, disabled as this is standard for MSBuild tasks.
#pragma warning disable CA1819

/// <summary>A MSBuild task to generate code from Protobuf files using <c>protoc</c> C# built-in generator and
/// <c>protoc-gen-icerpc-csharp</c> generator.</summary>
public class ProtocTask : ToolTask
{
    /// <summary>Gets or sets the output directory for the generated code; corresponds to the
    /// <c>--icerpc-csharp_out=</c> option of the <c>protoc</c> compiler.</summary>
    [Required]
    public string OutputDir { get; set; } = "";

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

    /// <summary>Gets or sets the directory containing the protoc-gen-icerpc-csharp scripts.</summary>
    [Required]
    public string GenPath { get; set; } = "";

    /// <summary>Gets or sets the directory containing the protoc-gen-icerpc-build-telemetry scripts.</summary>
    [Required]
    public string BuildTelemetryPath { get; set; } = "";

    /// <summary>Gets or sets a value indicating whether to run the build telemetry plug-in.</summary>
    [Required]
    public bool RunBuildTelemetry { get; set; }

    /// <summary>Gets or sets the working directory for executing the protoc compiler from.</summary>
    [Required]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1721:Property names should not match get methods",
        Justification = "This is by design, see ToolTask.GetWorkingDirectory documentation.")]
    public string WorkingDirectory { get; set; } = "";

    /// <summary>The computed SHA-256 hash of the Protobuf files.</summary>
    [Output]
    public string? CompilationHash { get; set; }

    /// <inheritdoc/>
    protected override string ToolName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "protoc.exe" : "protoc";

    /// <inheritdoc/>
    protected override string GenerateCommandLineCommands()
    {
        var builder = new CommandLineBuilder(false);

        // Specify the full path to the protoc-gen-icerpc-csharp script.

        string genScriptName =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                "protoc-gen-icerpc-csharp.bat" : "protoc-gen-icerpc-csharp.sh";
        builder.AppendSwitch("--plugin");
        builder.AppendFileNameIfNotNull($"protoc-gen-icerpc-csharp={Path.Combine(GenPath, genScriptName)}");

        // Add --csharp_out to generate Protobuf C# code
        builder.AppendSwitch("--csharp_out");
        builder.AppendFileNameIfNotNull(OutputDir);

        // Add --icerpc-csharp_out to generate IceRPC + Protobuf integration code
        builder.AppendSwitch("--icerpc-csharp_out");
        builder.AppendFileNameIfNotNull(OutputDir);

        if (RunBuildTelemetry)
        {
            // Enable build telemetry
            string buildTelemetryScriptName =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                    "protoc-gen-icerpc-build-telemetry.bat" : "protoc-gen-icerpc-build-telemetry.sh";

            // Specify the full path to the protoc-gen-icerpc-build-telemetry script.
            builder.AppendSwitch("--plugin");
            builder.AppendFileNameIfNotNull(
                $"protoc-gen-icerpc-build-telemetry={Path.Combine(BuildTelemetryPath, buildTelemetryScriptName)}");

            // Add --icerpc-build-telemetry_out to enable build telemetry, even though the build telemetry doesn't
            // generate any output files.
            builder.AppendSwitch("--icerpc-build-telemetry_out");
            builder.AppendFileNameIfNotNull(OutputDir);
        }

        var searchPath = new List<string>(SearchPath);

        // Add the sources directories to the import search path
        var computedSources = new List<ITaskItem>();
        foreach (ITaskItem source in Sources)
        {
            string fullPath = source.GetMetadata("FullPath");
            string? directory = Path.GetDirectoryName(fullPath);
            if (directory is not null && !searchPath.Contains(directory))
            {
                searchPath.Add(directory);
            }

            // Add dependency_out to generate dependency files
            builder.AppendSwitch("--dependency_out");
            builder.AppendFileNameIfNotNull(
                Path.Combine(OutputDir, $"{source.GetMetadata("FileName").ToPascalCase()}.d"));
        }

        // Add protoc searchPath paths
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
        try
        {
            Debug.Assert(singleLine is not null);
            if (singleLine.Contains(':', StringComparison.Ordinal))
            {
                // We assume it's about a file.
                string[] parts = singleLine.Split([':'], 4);
                string fileName = parts[0];
                int lineNumber = int.Parse(parts[1], CultureInfo.InvariantCulture);
                int columnNumber = int.Parse(parts[2], CultureInfo.InvariantCulture);
                string errorMessage = parts[3];

                Log.LogError("", "", "", fileName, lineNumber, columnNumber, -1, -1, errorMessage);
            }
            else
            {
                Log.LogMessage(singleLine);
            }
        }
        catch (FormatException)
        {
            Log.LogError(singleLine, messageImportance);
        }
    }

    /// <inheritdoc/>
    protected override void LogToolCommand(string message) => Log.LogMessage(MessageImportance.Normal, message);
}
