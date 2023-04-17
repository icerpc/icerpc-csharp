// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace IceRpc.Slice.Tools;

/// <summary>A MSbuild task to compile Slice files to C# using the IceRPC <c>slicec-cs</c> compiler.</summary>
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
    public string[] ReferencedFiles { get; set; } = Array.Empty<string>();

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

    /// <inheritdoc/>
    protected override string GenerateCommandLineCommands()
    {
        var builder = new CommandLineBuilder(false);

        if (OutputDir.Length > 0)
        {
            builder.AppendSwitch("--output-dir");
            builder.AppendFileNameIfNotNull(OutputDir);
        }

        foreach (string path in ReferencedFiles)
        {
            builder.AppendSwitchIfNotNull("-R", path);
        }

        foreach (string option in AdditionalOptions)
        {
            builder.AppendTextUnquoted(" ");
            builder.AppendTextUnquoted(option);
        }
        builder.AppendSwitch("--diagnostic-format=json");
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
            // Ignore messages from stdout, messageImportance is set to MessageImportance.Low for messages from stdout.
            return;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        if (JsonSerializer.Deserialize<Diagnostic>(singleLine, options) is Diagnostic diagnostic)
        {
            LogSliceCompilerDiagnostic(
                diagnostic.Severity,
                diagnostic.Message,
                diagnostic.ErrorCode,
                diagnostic.Span.File,
                diagnostic.Span.Start,
                diagnostic.Span.End);

            // Log notes as additional error/warnings.
            foreach (Note note in diagnostic.Notes)
            {
                LogSliceCompilerDiagnostic(
                    diagnostic.Severity,
                    note.Message,
                    diagnostic.ErrorCode,
                    note.SourceSpan.File,
                    note.SourceSpan.Start,
                    note.SourceSpan.End);
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
                Log.LogError("", code ?? "E000", "", file, start.Row, start.Column, end.Row, end.Column, message);
            }
            else
            {
                Log.LogWarning("", code ?? "W000", "", file, start.Row, start.Column, end.Row, end.Column, message);
            }
        }
    }

    /// <inheritdoc/>
    protected override void LogToolCommand(string message) => Log.LogMessage(MessageImportance.Normal, message);
}

public class Diagnostic
{
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "";
    public SourceSpan Span { get; set; } = new SourceSpan();
    public Note[] Notes { get; set; } = Array.Empty<Note>();

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; } = null;
}

public class Note
{
    public string Message { get; set; } = "";
    [JsonPropertyName("span")]
    public SourceSpan SourceSpan { get; set; } = new SourceSpan();
}

public class SourceSpan
{
    public Location Start { get; set; }
    public Location End { get; set; }
    public string File { get; set; } = "";
}

public struct Location
{
    public int Row { get; set; }
    [JsonPropertyName("col")]
    public int Column { get; set; }
}
