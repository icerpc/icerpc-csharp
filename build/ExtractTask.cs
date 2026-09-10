// Copyright (c) ZeroC, Inc.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>A custom MSBuild task that extracts .zip archive files to a destination folder. This tasks uses
/// <see cref="ZipFile.ExtractToDirectory(string, string)"/> to extract the files, which ensures that Unix permissions
/// are correctly restored. The MSBuild Unzip task does not restore Unix permissions.</summary>
/// <remarks>The archives are extracted into a sibling staging folder that replaces the destination folder only once
/// all of them are extracted, so that an interrupted build does not leave a partially extracted destination folder
/// behind.</remarks>
public class ExtractTask : Task
{
    /// <summary>Gets or sets a <see cref="ITaskItem"/> with a destination folder path to unzip the files to. An
    /// existing destination folder is replaced.</summary>
    [Required]
    public ITaskItem DestinationFolder { get; set; }

    /// <summary>Gets or sets an array of <see cref="ITaskItem"/> objects containing the paths to .zip archive files to
    /// unzip.</summary>
    [Required]
    public ITaskItem[] SourceFiles { get; set; }

    /// <summary>Extract the .zip archive files to the destination folder.</summary>
    /// <returns>Returns whether or not the execution completed successfully.</returns>
    public override bool Execute()
    {
        string destinationFolder =
            DestinationFolder.ItemSpec.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string stagingFolder = destinationFolder + ".extracting";

        DeleteFolder(stagingFolder);
        try
        {
            foreach (ITaskItem sourceFile in SourceFiles)
            {
                ZipFile.ExtractToDirectory(sourceFile.ItemSpec, stagingFolder);
            }
            DeleteFolder(destinationFolder);
            Directory.Move(stagingFolder, destinationFolder);
        }
        finally
        {
            DeleteFolder(stagingFolder);
        }
        return true;
    }

    private static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
