// Copyright (c) ZeroC, Inc.

using IceRpc.CaseConverter.Internal;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace IceRpc.Protobuf.Tools;

// Properties should not return arrays, disabled as this is standard for MSBuild tasks.
#pragma warning disable CA1819

/// <summary>A MSBuild task to compute what Protobuf files have to be rebuild by <c>protoc</c>.</summary>
public class UpToDateCheckTask : Microsoft.Build.Utilities.Task
{
    /// <summary>Gets or sets additional input files that every source depends on, typically the <c>protoc</c>
    /// compiler and the code generator plug-in. A source is out of date when any of these files is missing or is
    /// newer than one of the source's outputs.</summary>
    public ITaskItem[] AdditionalInputs { get; set; } = [];

    /// <summary>Gets or sets a string that identifies the configuration used to generate the outputs, such as the
    /// compiler and plug-in versions and the options passed to them. When this value differs from the content of
    /// <see cref="FingerprintFile"/>, every source is out of date.</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>Gets or sets the path of the file that holds the <see cref="Fingerprint"/> recorded by the previous
    /// successful build. A missing file counts as a changed fingerprint. When empty, the fingerprint check is
    /// skipped.</summary>
    public string FingerprintFile { get; set; } = "";

    /// <summary>Gets or sets the output directory for the generated code.</summary>
    [Required]
    public string OutputDir { get; set; } = "";

    /// <summary>Gets or sets the Protobuf source files to compute if they are up to date.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>Gets the computed sources, which are equivalent to the <see cref="Sources"/> but carry additional
    /// metadata.</summary>
    [Output]
    public ITaskItem[] ComputedSources { get; private set; } = [];

    /// <summary>Computes whether or not an output file is up to date or needs to be rebuilt. After executing this
    /// task, <see cref="ComputedSources"/> contains a task item for each item in <see cref="Sources"/> with two
    /// additional metadata entries. The <c>UpToDate</c> metadata is set to 'true' or 'false', indicating whether the
    /// item is up to date or needs to be rebuilt. The <c>OutputFileName</c> metadata contains the base file name for
    /// the generated outputs. This is the input item's file name without the extension, and converted to PascalCase.
    /// </summary>
    /// <remarks>A source is up to date only when the <see cref="Fingerprint"/> matches the recorded one, all of its
    /// outputs exist, every input recorded in its dependency file and every <see cref="AdditionalInputs"/> entry
    /// exists, and the newest input is older than the oldest output.</remarks>
    /// <returns>Returns <see langword="true"/> if the task was executed successfully, <see langword="false"/>
    /// otherwise.</returns>
    public override bool Execute()
    {
        bool fingerprintChanged = false;
        if (FingerprintFile.Length > 0)
        {
            fingerprintChanged =
                !File.Exists(FingerprintFile) || File.ReadAllText(FingerprintFile).Trim() != Fingerprint.Trim();
            if (fingerprintChanged)
            {
                Log.LogMessage(
                    MessageImportance.Normal,
                    "The protoc configuration changed since the previous build; all Proto files are out of date.");
            }
        }

        string[] additionalInputs = [.. AdditionalInputs.Select(item => item.GetMetadata("FullPath"))];

        var computedSources = new List<ITaskItem>();
        foreach (ITaskItem source in Sources)
        {
            string fileName = source.GetMetadata("FileName").ToProtocPascalCase();
            string dependOutput = Path.Combine(OutputDir, $"{fileName}.d");
            string[] outputs =
            [
                dependOutput,
                Path.Combine(OutputDir, $"{fileName}.cs"),
                Path.Combine(OutputDir, $"{fileName}.IceRpc.cs"),
            ];

            bool upToDate = !fingerprintChanged && IsUpToDate(source.ItemSpec, outputs, dependOutput, additionalInputs);

            var computedSource = new TaskItem(source.ItemSpec);
            source.CopyMetadataTo(computedSource);
            computedSource.SetMetadata("UpToDate", upToDate ? "true" : "false");
            computedSource.SetMetadata("OutputFileName", fileName);
            computedSource.SetMetadata("OutputDir", OutputDir);
            computedSources.Add(computedSource);
        }

        ComputedSources = [.. computedSources];
        return true;
    }

    private bool IsUpToDate(string source, string[] outputs, string dependOutput, string[] additionalInputs)
    {
        string? missingOutput = outputs.FirstOrDefault(output => !File.Exists(output));
        if (missingOutput is not null)
        {
            Log.LogMessage(MessageImportance.Low, $"'{source}' is out of date: output '{missingOutput}' is missing.");
            return false;
        }

        // The outputs are only up to date when all of them are newer than every input, so compare against the
        // oldest output rather than the newest.
        long oldestOutputTime = outputs.Min(output => File.GetLastWriteTime(output).Ticks);

        foreach (string input in ProcessDependencies(dependOutput).Concat(additionalInputs))
        {
            if (!File.Exists(input))
            {
                // File.GetLastWriteTime returns a placeholder date for missing files, which would otherwise make a
                // deleted import look older than the outputs.
                Log.LogMessage(MessageImportance.Low, $"'{source}' is out of date: input '{input}' is missing.");
                return false;
            }

            if (File.GetLastWriteTime(input).Ticks >= oldestOutputTime)
            {
                Log.LogMessage(
                    MessageImportance.Low,
                    $"'{source}' is out of date: input '{input}' is newer than one of its outputs.");
                return false;
            }
        }

        return true;
    }

    private static List<string> ProcessDependencies(string dependOutput)
    {
        var depends = new List<string>();
        string dependContents = File.ReadAllText(dependOutput);

        // Strip everything before and including "Xxx.cs:" (the output target).
        const string outputPrefix = ".cs:";
        int i = dependContents.IndexOf(outputPrefix, StringComparison.CurrentCultureIgnoreCase);
        if (i == -1 || i + outputPrefix.Length >= dependContents.Length)
        {
            return depends;
        }

        dependContents = dependContents[(i + outputPrefix.Length)..];

        // The Make depfile format uses '\' at end of line as a line continuation, and escapes
        // spaces inside paths as '\ '. Windows directory separators are emitted as literal '\'
        // (not escaped). We split on newlines, strip the trailing continuation '\' and whitespace,
        // then unescape '\ ' -> ' ' so paths containing spaces resolve correctly.
        foreach (string line in dependContents.Split('\n'))
        {
            string filePath = line.TrimEnd().TrimEnd('\\').Trim().Replace("\\ ", " ", StringComparison.Ordinal);
            if (!string.IsNullOrEmpty(filePath))
            {
                depends.Add(Path.GetFullPath(filePath));
            }
        }

        return depends;
    }
}
