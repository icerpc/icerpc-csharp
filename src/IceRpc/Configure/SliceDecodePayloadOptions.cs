// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Configure;

/// <summary>An option class to customize the decoding of a Slice-encoded request or response payloads.</summary>
public sealed record class SliceDecodePayloadOptions
{
    /// <summary>The default value for <see cref="MaxDepth"/> (100).</summary>
    public const int DefaultMaxDepth = 100;

    /// <summary>The default value for <see cref="MaxSegmentSize"/> (1MB).</summary>
    public const int DefaultMaxSegmentSize = 1024 * 1024;

    /// <summary>The activator to use when decoding Slice classes, exceptions and traits. When <c>null</c>, the
    /// decoding of a request or response payload uses the activator injected by the Slice generated code.</summary>
    public IActivator? Activator { get; set; }

    /// <summary>The maximum depth when decoding a type recursively.</summary>
    /// <value>A value greater than 0. The default is <see cref="DefaultMaxDepth"/>.</value>
    public int MaxDepth
    {
        get => _maxDepth;
        set => _maxDepth = value > 0 ? value :
            throw new ArgumentException("value must greater than 0", nameof(value));
    }

    /// <summary>The maximum size of a Slice payload segment, in bytes.</summary>
    /// <value>A value greater than 0. The default is <see cref="DefaultMaxSegmentSize"/>.</value>
    public int MaxSegmentSize
    {
        get => _maxSegmentSize;
        set => _maxSegmentSize = value > 0 ? value :
            throw new ArgumentException("value must greater than 0", nameof(value));
    }

    /// <summary>The invoker assigned to decoded proxies. When null, a proxy decoded from an incoming request gets
    /// <see cref="Proxy.DefaultInvoker"/> while a proxy decoded from an incoming response gets the invoker of the
    /// proxy that created the request.</summary>
    public IInvoker? ProxyInvoker { get; set; }

    /// <summary>The default decode payload options.</summary>
    public SliceStreamDecoderOptions StreamDecoderOptions { get; set; } = SliceStreamDecoderOptions.Default;

    internal static SliceDecodePayloadOptions Default { get; } = new();

    /// <summary>The options for decoding a Slice stream.</summary>

    private int _maxDepth = DefaultMaxDepth;
    private int _maxSegmentSize = DefaultMaxSegmentSize;
}
