// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf;

/// <summary>Default implementation of <see cref="IProtobufFeature" />.</summary>
public sealed class ProtobufFeature : IProtobufFeature
{
    /// <summary>Gets a <see cref="IProtobufFeature" /> with default values for all properties.</summary>
    public static IProtobufFeature Default { get; } = new DefaultProtobufFeature();

    /// <summary>Gets the maximum length of an encoded Protobuf message, in bytes.</summary>
    /// <value>The maximum length of a Protobuf message. Defaults to <c>1</c> MB.</value>
    public int MaxMessageLength { get; }

    /// <inheritdoc/>
    public ProtobufEncodeOptions? EncodeOptions { get; }

    /// <summary>Constructs a Protobuf feature.</summary>
    /// <param name="maxMessageLength">The maximum message length. Use <c>-1</c> to get the default value.</param>
    /// <param name="encodeOptions">The encode options.</param>
    /// <param name="defaultFeature">A feature that provides default values for all parameters. <see langword="null" />
    /// is equivalent to <see cref="Default" />.</param>
    public ProtobufFeature(
        int maxMessageLength = -1,
        ProtobufEncodeOptions? encodeOptions = null,
        IProtobufFeature? defaultFeature = null)
    {
        defaultFeature ??= Default;
        MaxMessageLength = maxMessageLength >= 0 ? maxMessageLength : defaultFeature.MaxMessageLength;
        EncodeOptions = encodeOptions ?? defaultFeature.EncodeOptions;
    }

    private class DefaultProtobufFeature : IProtobufFeature
    {
        public int MaxMessageLength => 1024 * 1024;

        public ProtobufEncodeOptions? EncodeOptions => null;
    }
}
