// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf;

/// <summary>Represents an attribute used to mark classes implementing Protobuf services.</summary>
/// <remarks>The Protobuf source generator implements <see cref="IDispatcher"/> for classes marked with this attribute.
/// The <see cref="AttributeUsageAttribute.Inherited"/> is set to <see langword="false"/> because we only need to
/// generate the <see cref="IDispatcher"/> implementation for classes including the attribute, and not for derived
/// classes.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProtobufServiceAttribute : Attribute
{
}
