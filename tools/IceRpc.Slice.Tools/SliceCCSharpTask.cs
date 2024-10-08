// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace IceRpc.Slice.Tools;

/// <summary>A MSBuild task to compile Slice files to C# using the IceRPC <c>slicec-cs</c> compiler.</summary>
public class SliceCCSharpTask : ToolTask
{
    /// <summary>Additional options to pass to the <c>slicec-cs</c> compiler.</summary>
    public string[] AdditionalOptions { get; set; } = Array.Empty<string>();

    /// <summary>The output directory for the generated code; corresponds to the <c>--output-dir</c> option of the
    /// <c>slicec-cs</c> compiler.</summary>
    [Required]
    public string OutputDir { get; set; } = "";

    /// <summary>The files that are needed for referencing, but that no code should be generated for them, corresponds
    /// to <c>-R</c> slicec-cs compiler option.</summary>
    public string[] References { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The RPC provider to generate code for, corresponds to the <c>--rpc</c> slicec-cs compiler option.
    /// </summary>
    [Required]
    public string Rpc { get; set; } = "";

    /// <summary>The Slice files to compile, these are the input files pass to the slicec-cs compiler.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>The directory containing the slicec-cs compiler.</summary>
    [Required]
    public string ToolsPath { get; set; } = "";

    /// <summary>The working directory for executing the slicec-cs compiler from.</summary>
    [Required]
    public string WorkingDirectory { get; set; } = "";

    /// <inheritdoc/>
    protected override string ToolName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "slicec-cs.exe" : "slicec-cs";

    /// <summary>The computed SHA-256 hash of the Slice files.</summary>
    [Output]
    public string? CompilationHash { get; set; }

    /// <summary>Whether the Slice compilation contained any Slice1 files.</summary>
    [Output]
    public bool ContainsSlice1 { get; set; }

    /// <summary>Whether the Slice compilation contained any Slice1 files.</summary>
    [Output]
    public bool ContainsSlice2 { get; set; }

    /// <summary>The number of source files in the Slice compilation.</summary>
    [Output]
    public int SourceFileCount { get; set; }

    /// <summary>The number of reference files in the Slice compilation.</summary>
    [Output]
    public int ReferenceFileCount { get; set; }

    private readonly JsonSerializerOptions _jsonSerializeOptions = new() { PropertyNameCaseInsensitive = true };

    protected override bool ValidateParameters()
    {
        var validRpcValues = new[] { "icerpc", "none" };

        if (!validRpcValues.Contains(Rpc, StringComparer.OrdinalIgnoreCase))
        {
            Log.LogError($"Invalid Rpc value '{Rpc}'. Valid values are 'icerpc' and 'none'.");
            return false;
        }

        Rpc = Rpc.ToLowerInvariant();

        return base.ValidateParameters();
    }

    /// <inheritdoc/>
    protected override string GenerateCommandLineCommands()
    {
        var builder = new CommandLineBuilder(false);

        if (OutputDir.Length > 0)
        {
            builder.AppendSwitch("--output-dir");
            builder.AppendFileNameIfNotNull(OutputDir);
        }

        foreach (string reference in References)
        {
            builder.AppendSwitchIfNotNull("-R", reference);
        }

        foreach (string option in AdditionalOptions)
        {
            builder.AppendTextUnquoted(" ");
            builder.AppendTextUnquoted(option);
        }
        builder.AppendSwitch("--diagnostic-format=json");
        builder.AppendSwitch("--telemetry");
        builder.AppendSwitch($"--rpc={Rpc}");
        builder.AppendFileNamesIfNotNull(
            Sources.Select(item => item.GetMetadata("FullPath").ToString()).ToArray(),
            " ");

        return builder.ToString();
    }

    /// <inheritdoc/>
    protected override string GenerateFullPathToTool()
    {
        string path = Path.Combine(ToolsPath, ToolName);
        if (!File.Exists(path))
        {
            Log.LogError($"Slice compiler '{path}' not found.");
        }
        return path;
    }

    /// <inheritdoc/>
    protected override string GetWorkingDirectory() => WorkingDirectory;

    /// <summary> Process the diagnostics emitted by the slicec-cs compiler and log them with the MSBuild logger.
    /// </summary>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance)
    {
        if (messageImportance == MessageImportance.Low)
        {
            // Messages from stdout
            if (JsonSerializer.Deserialize<BuildTelemetry>(
                singleLine,
                _jsonSerializeOptions) is BuildTelemetry buildTelemetry)
            {
                var jsonDoc = System.Text.Json.JsonDocument.Parse(singleLine);
                CompilationHash = buildTelemetry.CompilationHash;
                ContainsSlice1 = buildTelemetry.ContainsSlice1;
                ContainsSlice2 = buildTelemetry.ContainsSlice2;
                SourceFileCount = buildTelemetry.SourceFileCount;
                ReferenceFileCount = buildTelemetry.ReferenceFileCount;
            }
        }
        else if (JsonSerializer.Deserialize<Diagnostic>(singleLine, _jsonSerializeOptions) is Diagnostic diagnostic)
        {
            diagnostic.SourceSpan ??= new SourceSpan();
            LogSliceCompilerDiagnostic(
                diagnostic.Severity,
                diagnostic.Message,
                diagnostic.ErrorCode,
                diagnostic.SourceSpan.File,
                diagnostic.SourceSpan.Start,
                diagnostic.SourceSpan.End);

            foreach (Note note in diagnostic.Notes)
            {
                note.SourceSpan ??= diagnostic.SourceSpan;
                Log.LogMessage(
                    "",
                    "",
                    "",
                    note.SourceSpan.File,
                    note.SourceSpan.Start.Row,
                    note.SourceSpan.Start.Column,
                    note.SourceSpan.End.Row,
                    note.SourceSpan.End.Column,
                    MessageImportance.High,
                    note.Message);
            }
        }

        void LogSliceCompilerDiagnostic(
            string severity,
            string message,
            string? code,
            string file,
            Location start,
            Location end)
        {
            if (severity == "error")
            {
                Log.LogError("", code, "", file, start.Row, start.Column, end.Row, end.Column, message);
            }
            else
            {
                Debug.Assert(severity == "warning");
                Log.LogWarning("", code, "", file, start.Row, start.Column, end.Row, end.Column, message);
            }
        }
    }

    /// <inheritdoc/>
    protected override void LogToolCommand(string message) => Log.LogMessage(MessageImportance.Normal, message);
}
