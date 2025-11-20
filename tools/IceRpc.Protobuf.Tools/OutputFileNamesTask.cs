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

/// <summary>A MSBuild task to compute the C# file name generated from a given proto file.</summary>
public class OutputFileNamesTask : Task
{
    /// <summary>Gets or sets the Protobuf source files.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>Gets the Protobuf computed sources. The computed sources are equal to the <see cref="Sources" /> but
    /// with the additional <c>OutputFileName</c> metadata.</summary>
    [Output]
    public ITaskItem[] ComputedSources { get; private set; } = [];

    public override bool Execute()
    {
        var computedSources = new List<ITaskItem>();
        foreach (ITaskItem source in Sources)
        {
            string fullPath = source.GetMetadata("FullPath");
            var computedSource = new TaskItem(source.ItemSpec);
            source.CopyMetadataTo(computedSource);
            computedSource.SetMetadata("OutputFileName", Path.GetFileNameWithoutExtension(fullPath).ToPascalCase());
            computedSources.Add(computedSource);
        }
        ComputedSources = [.. computedSources];
        return true;
    }
}
