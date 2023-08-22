// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Represents an attribute used to mark classes implementing Slice services.</summary>
/// <remarks>The Slice source generator implements <see cref="IDispatcher"/> for classes marked with this attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SliceServiceAttribute : Attribute
{
}
