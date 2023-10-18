// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Protobuf;

/// <summary>Represents an attribute that protoc-gen-icerpc-csharp uses to mark helper methods it generates in Service
/// interfaces.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProtobufOperationAttribute : Attribute
{
    /// <summary>Gets the operation name.</summary>
    /// <value>The operation name.</value>
    public string Value { get; }

    /// <summary>Constructs a Protobuf operation attribute.</summary>
    /// <param name="value">The operation name.</param>>
    public ProtobufOperationAttribute(string value) => Value = value;
}
