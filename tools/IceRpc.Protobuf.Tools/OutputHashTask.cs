// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IceRpc.Protobuf.Tools;

/// <summary>A MSBuild task to compute the SHA-256 hash of the Protobuf files.</summary>
public class OutputHashTask : Task
{
    /// <summary>Gets or sets the Protobuf source files.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>The computed SHA-256 hash of the Protobuf files.</summary>
    [Output]
    public string? CompilationHash { get; set; }

    /// <summary>The number of protobuf files in the compilation.</summary>
    [Output]
    public string? FileCount { get; set; }

    /// <inheritdoc/>
    public override bool Execute()
    {
        using var sha256 = SHA256.Create();

        // Compute the SHA-256 hash of each file
        byte[] combinedHashBytes = Sources
            .Select(source =>
            {
                byte[] fileBytes = File.ReadAllBytes(source.GetMetadata("FullPath"));
                byte[] hashBytes = sha256.ComputeHash(fileBytes);
                return hashBytes;
            })
            .SelectMany(hashBytes => hashBytes)
            .ToArray();

        // Compute the SHA-256 hash of the combined hash bytes
        byte[] aggregatedHashBytes = sha256.ComputeHash(combinedHashBytes);

        // Convert the aggregated hash bytes to a string
        string aggregatedHash = ToHexString(aggregatedHashBytes);

        CompilationHash = aggregatedHash;
        FileCount = Sources.Length.ToString();
        return true;
    }

    /// <summary>Converts a byte array to a hexadecimal string.</summary>
    private static string ToHexString(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

}
