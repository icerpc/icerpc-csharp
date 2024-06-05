// Copyright (c) ZeroC, Inc.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace IceRpc.Protobuf.Tools;

/// <summary>A MSBuild task to compute the SHA-256 hash of the Protobuf files.</summary>
public class OutputHashTask : Task
{
    /// <summary>Gets or sets the Protobuf source files.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>The computed SHA-256 hash of the Protobuf files.</summary>
    [Output]
    public string? OutputHash { get; set; }

    /// <inheritdoc/>
    public override bool Execute()
    {
        using SHA256 sha256 = SHA256.Create();
        string aggregatedHash = Sources
            .Select(source =>
            {
                byte[] fileBytes = File.ReadAllBytes(source.GetMetadata("FullPath"));
                byte[] hashBytes = sha256.ComputeHash(fileBytes);
                return HexStringConverter.ToHexString(hashBytes);
            })
            .Aggregate((current, next) => current + next);

        OutputHash = aggregatedHash;
        return true;
    }
}
