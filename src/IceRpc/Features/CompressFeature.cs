// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Default implementation of <see cref="ICompressFeature" />.</summary>
public sealed class CompressFeature : ICompressFeature
{
    /// <summary>Gets an <see cref="ICompressFeature" /> with <see cref="Value" /> set to <see langword="true" />.
    /// </summary>
    /// <value>A shared <see cref="ICompressFeature" /> with <see cref="Value" /> set to <see langword="true" />.
    /// </value>
    public static ICompressFeature Compress { get; } = new CompressFeature(true);

    /// <summary>Gets an <see cref="ICompressFeature" /> with <see cref="Value" /> set to <see langword="false" />.
    /// </summary>
    /// <value>A shared <see cref="ICompressFeature" /> with <see cref="Value" /> set to <see langword="false" />.
    /// </value>
    public static ICompressFeature DoNotCompress { get; } = new CompressFeature(false);

    /// <inheritdoc/>
    public bool Value { get; }

    private CompressFeature(bool value) => Value = value;
}
