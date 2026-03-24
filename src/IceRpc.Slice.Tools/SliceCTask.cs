// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace IceRpc.Slice.Tools;

/// <summary>A MSBuild task to compile Slice files using the <c>slicec</c> compiler with configured generator
/// plugins.</summary>
public class SliceCTask : ToolTask
{
    /// <summary>Additional options to pass to the <c>slicec</c> compiler.</summary>
    public string[] AdditionalOptions { get; set; } = [];

    /// <summary>The directory containing the <c>slicec-gen-icerpc-slice-csharp</c> generator scripts.</summary>
    [Required]
    public string IceRpcGenPath { get; set; } = "";

    /// <summary>The output directory for the generated code; corresponds to the <c>--output-dir</c> option of the
    /// <c>slicec</c> compiler.</summary>
    [Required]
    public string OutputDir { get; set; } = "";

    /// <summary>The files that are needed for referencing, but that no code should be generated for them, corresponds
    /// to <c>-R</c> slicec compiler option.</summary>
    public string[] References { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether to run the IceRpc.Slice.Generator plugin in addition to
    /// ZeroC.Slice.Generator.</summary>
    [Required]
    public bool Rpc { get; set; }

    /// <summary>The Slice files to compile, these are the input files pass to the slicec compiler.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>The directory containing the slicec compiler.</summary>
    [Required]
    public string ToolsPath { get; set; } = "";

    /// <summary>The working directory for executing the slicec compiler from.</summary>
    [Required]
    [SuppressMessage("Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Part of the public API")]
    public string WorkingDirectory { get; set; } = "";

    /// <summary>The directory containing the <c>slicec-gen-zeroc-slice-csharp</c> generator scripts.</summary>
    [Required]
    public string ZeroCGenPath { get; set; } = "";

    /// <inheritdoc/>
    protected override string ToolName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "slicec.exe" : "slicec";

    private readonly JsonSerializerOptions _jsonSerializeOptions = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc/>
    protected override string GenerateCommandLineCommands()
    {
        var builder = new CommandLineBuilder(false);

        // Always add the ZeroC.Slice.Generator plugin.
        string zeroCGenScriptName =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                "slicec-gen-zeroc-slice-csharp.bat" : "slicec-gen-zeroc-slice-csharp.sh";
        builder.AppendSwitch("--plugin");
        builder.AppendFileNameIfNotNull(
            $"zeroc-slice-csharp={Path.Combine(ZeroCGenPath, zeroCGenScriptName)}");

        // Add the IceRpc.Slice.Generator plugin when Rpc is true.
        if (Rpc)
        {
            string iceRpcGenScriptName =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                    "slicec-gen-icerpc-slice-csharp.bat" : "slicec-gen-icerpc-slice-csharp.sh";
            builder.AppendSwitch("--plugin");
            builder.AppendFileNameIfNotNull(
                $"icerpc-slice-csharp={Path.Combine(IceRpcGenPath, iceRpcGenScriptName)}");
        }

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

    /// <summary> Process the diagnostics emitted by the slicec compiler and log them with the MSBuild logger.
    /// </summary>
    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance)
    {
        if (JsonSerializer.Deserialize<Diagnostic>(singleLine, _jsonSerializeOptions) is Diagnostic diagnostic)
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
