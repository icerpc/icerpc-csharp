// Copyright (c) ZeroC, Inc.

using IceRpc.CaseConverter.Internal;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace IceRpc.Protobuf.Tools;

// Properties should not return arrays, disabled as this is standard for MSBuild tasks.
#pragma warning disable CA1819

/// <summary>A MSBuild task to compute what Protobuf files have to be rebuild by <c>protoc</c>.</summary>
public class UpToDateCheckTask : Microsoft.Build.Utilities.Task
{
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
    /// <returns>Returns <see langword="true"/> if the task was executed successfully, <see langword="false"/>
    /// otherwise.</returns>
    public override bool Execute()
    {
        var computedSources = new List<ITaskItem>();

        foreach (ITaskItem source in Sources)
        {
            bool upToDate = true;
            string fileName = source.GetMetadata("FileName").ToPascalCase();
            string dependOutput = Path.Combine(OutputDir, $"{fileName}.d");
            string csharpOutput = Path.Combine(OutputDir, $"{fileName}.cs");
            string icerpcOutput = Path.Combine(OutputDir, $"{fileName}.IceRpc.cs");

            if (File.Exists(dependOutput) && File.Exists(csharpOutput) && File.Exists(icerpcOutput))
            {
                long lastWriteTime = Math.Max(
                    File.GetLastWriteTime(dependOutput).Ticks,
                    File.GetLastWriteTime(csharpOutput).Ticks);
                lastWriteTime = Math.Max(lastWriteTime, File.GetLastWriteTime(icerpcOutput).Ticks);
                List<string> dependencies = ProcessDependencies(dependOutput);
                foreach (string dependency in dependencies)
                {
                    if (File.GetLastWriteTime(dependency).Ticks >= lastWriteTime)
                    {
                        // If a dependency is newer than any of the outputs the source is not up to date.
                        upToDate = false;
                        break;
                    }
                }
            }
            else
            {
                // If any of the outputs is missing the file is not up to date.
                upToDate = false;
            }

            var computedSource = new TaskItem(source.ItemSpec);
            source.CopyMetadataTo(computedSource);
            computedSource.SetMetadata("UpToDate", upToDate ? "true" : "false");
            computedSource.SetMetadata("OutputFileName", fileName);
            computedSource.SetMetadata("OutputDir", OutputDir);
            computedSources.Add(computedSource);
        }

        ComputedSources = [.. computedSources];
        return true;

        static List<string> ProcessDependencies(string dependOutput)
        {
            var depends = new List<string>();
            string dependContents = File.ReadAllText(dependOutput);
            // strip everything before Xxx.cs:
            const string outputPrefix = ".cs:";
            int i = dependContents.IndexOf(outputPrefix, StringComparison.CurrentCultureIgnoreCase);
            if (i != -1 && i + outputPrefix.Length < dependContents.Length)
            {
                dependContents = dependContents[(i + outputPrefix.Length)..];
                foreach (string line in dependContents.Split(['\\']))
                {
                    string filePath = line.Trim();
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        depends.Add(Path.GetFullPath(filePath));
                    }
                }
            }
            return depends;
        }
    }
}
