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

namespace Slice.Builder.MSBuild;

/// <summary>A MSbuild task to compile Slice files using IceRpc slicec-cs compiler.</summary>
public class SliceCCSharpTask : ToolTask
{
    /// <summary>Additional options to pass to the slicec compiler.</summary>
    public string[] AdditionalOptions { get; set; } = Array.Empty<string>();

    /// <summary>The output directory for generated code, correspond to the <c>--output-dir</c> slicec-cs compiler
    /// option.</summary>
    [Required]
    public string OutputDir { get; set; } = "";

    /// <summary>The files that are needed for referencing, but that no code should be generated for them,
    /// corresponds to <c>-R</c> slicec-cs compiler option.</summary>
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
            Log.LogError($"Slice compiler '{path}' not found. Review the IceRpcToolsPath setting");
        }
        return path;
    }

    /// <inheritdoc/>
    protected override string GetWorkingDirectory() => WorkingDirectory;

    /// <summary> Process the diagnostics emitted by the slicec-cs compiler and send them to the MSBuild logger.
    /// </summary>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance)
    {
        if (messageImportance == MessageImportance.Low)
        {
            // Ignore all messages from stdout, messageImportance is set to MessageImportance.Low for messages from
            // stdout.
            return;
        }

        // Deserialize JSON
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        try
        {
            Diagnostic? deserializeDiagnostic = JsonSerializer.Deserialize<Diagnostic>(singleLine, options);
            /// Parse the JSON
            if (deserializeDiagnostic is Diagnostic diagnostic)
            {
                // Log the baseline Error or Warning
                switch (diagnostic.Severity)
                {
                    case "error":
                        {
                            Log.LogError(
                                "",
                                diagnostic.ErrorCode ?? "E000",
                                "",
                                diagnostic.Span?.File,
                                diagnostic.Span?.Start.Row ?? 0,
                                diagnostic.Span?.Start.Col ?? 0,
                                diagnostic.Span?.End.Row ?? 0,
                                diagnostic.Span?.End.Col ?? 0,
                                diagnostic.Message);
                            break;
                        }
                    case "warning":
                        {
                            Log.LogWarning(
                                "",
                                diagnostic.ErrorCode ?? "W000",
                                "",
                                diagnostic.Span?.File,
                                diagnostic.Span?.Start.Row ?? 0,
                                diagnostic.Span?.Start.Col ?? 0,
                                diagnostic.Span?.End.Row ?? 0,
                                diagnostic.Span?.End.Col ?? 0,
                                diagnostic.Message);
                            break;
                        }
                    default:
                        {
                            break;
                        }
                }

                // Log additional notes as messages
                foreach (Note note in diagnostic.Notes ?? Enumerable.Empty<Note>())
                {
                    Log.LogMessage(
                        "",
                        diagnostic.ErrorCode ?? "E000",
                        "",
                        note.Span?.File,
                        note.Span?.Start.Row ?? 0,
                        note.Span?.Start.Col ?? 0,
                        note.Span?.End.Row ?? 0,
                        note.Span?.End.Col ?? 0,
                        MessageImportance.High,
                        note.Message);
                }
            }

        }
        catch (JsonException)
        {
            // If we failed to parse the message as Json this is either a compiler panic, or a error from the
            // compiler that was not correctly formatted.
            Log.LogError(
                "",
                "",
                "",
                "",
                0,
                0,
                0,
                0,
                singleLine);
        }
    }

    /// <inheritdoc/>
    protected override void LogToolCommand(string message) => Log.LogMessage(MessageImportance.Low, message);
}

public class Diagnostic
{
    public string Message { get; set; } = default!;
    public string Severity { get; set; } = default!;
    public Span Span { get; set; } = default!;
    public Note[] Notes { get; set; } = default!;

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; } = default!;
}

public class Note
{
    public string Message { get; set; } = default!;
    public Span Span { get; set; } = default!;
}

public class Span
{
    public Location Start { get; set; } = default!;
    public Location End { get; set; } = default!;
    public string File { get; set; } = default!;
}

public class Location
{
    public int Row { get; set; }
    public int Col { get; set; }
}
