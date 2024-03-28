// Copyright (c) ZeroC, Inc.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class ExtractTask : Task
{
    [Required]
    public ITaskItem DestinationFolder { get; set; }

    [Required]
    public ITaskItem[] SourceFiles { get; set; }

    public override bool Execute()
    {
        foreach (ITaskItem sourceFile in SourceFiles)
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(sourceFile.ItemSpec, DestinationFolder.ItemSpec);
        }

        return true;
    }
}
