// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Protobuf;

/// <summary>Represents an attribute that protoc-gen-icerpc-csharp applies to abstract methods in Service interfaces.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProtobufOperationAttribute : Attribute
{
    /// <summary>Gets the operation name. It corresponds to the name of the rpc method in the Protobuf file, with the
    /// same spelling and the same case.</summary>
    /// <value>The operation name.</value>
    public string Value { get; }

    /// <summary>Constructs a Protobuf operation attribute.</summary>
    /// <param name="value">The operation name.</param>>
    public ProtobufOperationAttribute(string value) => Value = value;
}
