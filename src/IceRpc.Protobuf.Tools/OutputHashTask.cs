// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IceRpc.Protobuf.Tools;

// Properties should not return arrays, disabled as this is standard for MSBuild tasks.
#pragma warning disable CA1819

/// <summary>A MSBuild task to compute the SHA-256 hash of the Protobuf files.</summary>
public class OutputHashTask : Microsoft.Build.Utilities.Task
{
    /// <summary>Gets or sets the Protobuf source files.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>The computed SHA-256 hash of the Protobuf files.</summary>
    [Output]
    public string? CompilationHash { get; set; }

    /// <summary>The number of protobuf files in the compilation.</summary>
    [Output]
    public uint? FileCount { get; set; }

    /// <inheritdoc/>
    public override bool Execute()
    {

        // Compute the SHA-256 hash of each file
        byte[] combinedHashBytes = [.. Sources
            .Select(source =>
            {
                byte[] fileBytes = File.ReadAllBytes(source.GetMetadata("FullPath"));
                byte[] hashBytes = SHA256.HashData(fileBytes);
                return hashBytes;
            })
            .SelectMany(hashBytes => hashBytes)];

        // Compute the SHA-256 hash of the combined hash bytes
        byte[] aggregatedHashBytes = SHA256.HashData(combinedHashBytes);

        // Convert the aggregated hash bytes to a string
        string aggregatedHash = ToHexString(aggregatedHashBytes);

        CompilationHash = aggregatedHash;
        FileCount = (uint)Sources.Length;
        return true;
    }

    /// <summary>Converts a byte array to a hexadecimal string.</summary>
    private static string ToHexString(byte[] bytes) => string.Concat(bytes.Select(b => $"{b:x2}"));

}
