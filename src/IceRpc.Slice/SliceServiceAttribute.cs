// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Slice;

/// <summary>Represents an attribute used to mark classes implmenting Slice services. The Slice source generator
/// implements <see cref="IDispatcher"/> for classes marked with this attribute.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SliceServiceAttribute : Attribute
{
}
