// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Protobuf;

/// <summary>Represents an attribute that protoc-gen-icerpc-csharp applies to abstract methods in Service interfaces.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProtobufMethodAttribute : Attribute
{
    /// <summary>Gets the method name. It corresponds to the name of the rpc method in the Protobuf file, with the
    /// same spelling and the same case.</summary>
    /// <value>The operation name.</value>
    public string Value { get; }

    /// <summary>Constructs a Protobuf method attribute.</summary>
    /// <param name="value">The method name.</param>>
    public ProtobufMethodAttribute(string value) => Value = value;
}
