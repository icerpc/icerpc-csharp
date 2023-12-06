// Copyright (c) ZeroC, Inc.

using IceRpc.CaseConverter.Internal;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace IceRpc.Protobuf.Tools;

/// <summary>A MSBuild task to compute what Protobuf files have to be rebuild by <c>protoc</c>.</summary>
public class UpToDateCheckTask : Task
{
    /// <summary>Gets or sets the output directory for the generated code; corresponds to the
    /// <c>--icerpc-csharp_out=</c> option of the <c>protoc</c> compiler.</summary>
    [Required]
    public string OutputDir { get; set; } = "";

    /// <summary>Gets or sets the Protobuf source files to compute if they are up to date.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>Gets the computed sources, which are equal to the <see cref="Sources" /> but carring additional
    /// metadata.</summary>
    [Output]
    public ITaskItem[] ComputedSources { get; private set; } = Array.Empty<ITaskItem>();

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

            ITaskItem computedSource = new TaskItem(source.ItemSpec);
            source.CopyMetadataTo(computedSource);
            computedSource.SetMetadata("UpToDate", upToDate ? "true" : "false");
            computedSource.SetMetadata("ProtoGeneratedPath", csharpOutput);
            computedSource.SetMetadata("IceRpcGeneratedPath", icerpcOutput);
            computedSource.SetMetadata("DependGeneratedPath", dependOutput);
            computedSource.SetMetadata("OutputDir", OutputDir);
            computedSources.Add(computedSource);
        }

        ComputedSources = computedSources.ToArray();
        return true;

        static List<string> ProcessDependencies(string dependOutput)
        {
            var depends = new List<string>();
            string dependContents = File.ReadAllText(dependOutput);
            // strip everything before Xxx.cs:
            int i = dependContents.IndexOf(".cs:");
            if (i != -1 && i + 4 < dependContents.Length)
            {
                dependContents = dependContents.Substring(i + 4);
                foreach (string line in dependContents.Split(new char[] { '\\' }))
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
