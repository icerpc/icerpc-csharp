// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace IceRpc.Protobuf.Tools;

/// <summary>A MSBuild task to compile Protuf files with <c>icerpc-csharp</c> protoc plug-in.</summary>
public class ProtocTask : ToolTask
{
    /// <summary>The output directory for the generated code; corresponds to the <c>--icerpc-csharp_out=</c> option of the
    /// <c>protoc</c> compiler.</summary>
    [Required]
    public string OutputDir { get; set; } = "";

    /// <summary>The directories in which to search for imports, corresponds to <c>-I</c> protoc compiler option.</summary>
    public string[] ImportPath { get; set; } = Array.Empty<string>();

    /// <summary>The Protobuf files to compile, these are the input files pass to the protoc compiler.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>The directory containing the protoc compiler.</summary>
    [Required]
    public string ToolsPath { get; set; } = "";

    /// <summary>The directory containing the icerpc-csharp protoc plug-in.</summary>
    [Required]
    public string PluginPath { get; set; } = "";

    /// <summary>The working directory for executing the protoc compiler from.</summary>
    [Required]
    public string WorkingDirectory { get; set; } = "";

    /// <inheritdoc/>
    protected override string ToolName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "protoc.exe" : "protoc";

    /// <inheritdoc/>
    protected override string GenerateCommandLineCommands()
    {
        string protocPluginExe =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                "protoc-gen-icerpc-csharp.exe" : "protoc-gen-icerpc-csharp";

        var builder = new CommandLineBuilder(false);

        // Pass the full path to the icerpc-csharp protoc plugin
        builder.AppendSwitch("--plugin");
        builder.AppendFileNameIfNotNull($"protoc-gen-icerpc-csharp={Path.Combine(PluginPath, protocPluginExe)}");

        // Add --csharp_out to generate Protobuf C# code
        builder.AppendSwitch("--csharp_out");
        builder.AppendFileNameIfNotNull(OutputDir);

        // Add --icerpc-csharp_out to generate IceRpc C# + Protobuf integration code
        builder.AppendSwitch("--icerpc-csharp_out");
        builder.AppendFileNameIfNotNull(OutputDir);

        var importPath = new List<string>(ImportPath);
        // Add the sources directories to the import path
        foreach (ITaskItem source in Sources)
        {
            string directory = Path.GetDirectoryName(source.GetMetadata("FullPath"));
            if (!importPath.Contains(directory))
            {
                importPath.Add(directory);
            }
        }

        // Add protoc import paths
        foreach (string import in importPath)
        {
            builder.AppendSwitch("-I");
            builder.AppendFileNameIfNotNull(import);
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
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance) =>
        Log.LogError(singleLine, messageImportance);

    /// <inheritdoc/>
    protected override void LogToolCommand(string message) => Log.LogMessage(MessageImportance.Normal, message);
}
