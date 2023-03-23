// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>The default implementation for <see cref="ICompressFeature" />.</summary>
public sealed class CompressFeature : ICompressFeature
{
    /// <summary>Gets the <see cref="CompressFeature" /> instance that specifies that the payload of a request or
    /// response must be compressed.</summary>
    /// <value>The <see cref="CompressFeature" /> instance to specify that the payload should be compressed.</value>
    public static ICompressFeature Compress { get; } = new CompressFeature(true);

    /// <summary>Gets <see cref="CompressFeature" /> instance that specifies that the payload of a request or response
    /// must not be compressed.</summary>
    /// <value>The <see cref="CompressFeature" /> instance to specify that the payload should not be compressed.</value>
    public static ICompressFeature DoNotCompress { get; } = new CompressFeature(false);

    /// <inheritdoc/>
    public bool Value { get; }

    private CompressFeature(bool value) => Value = value;
}
