// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf;

/// <summary>Represents an attribute applied on classes implementing Protobuf services with IceRPC.</summary>
/// <remarks>The Protobuf Service source generator implements <see cref="IDispatcher"/> for classes with this
/// attribute - and not in derived classes unless they also carry this attribute.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProtobufServiceAttribute : Attribute
{
}
