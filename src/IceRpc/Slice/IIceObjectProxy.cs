// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>A helper interface that combines <see cref="IProxy" /> and <see cref="IIceObject" />.</summary>
/// <seealso cref="ProxyExtensions.AsAsync{TProxy}" />
public interface IIceObjectProxy : IIceObject, IProxy
{
}
