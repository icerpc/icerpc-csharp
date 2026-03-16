// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using System.ComponentModel;

namespace IceRpc.Ice;

/// <summary>Represents a fully sliced class instance. The <see cref="IActivator"/> used during decoding does not know
/// this type or any of its base classes.</summary>
public sealed class UnknownIceClass : IceClass
{
    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void DecodeCore(ref IceDecoder decoder)
    {
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void EncodeCore(ref IceEncoder encoder)
    {
    }

    internal UnknownIceClass()
    {
    }
}
