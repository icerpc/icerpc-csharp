// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Protobuf;

/// <summary>Represents an attribute that protoc-gen-icerpc-csharp applies to abstract methods in Service interfaces.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProtobufServiceMethodAttribute : Attribute
{
    /// <summary>Gets the method name. It corresponds to the name of the rpc method in the Protobuf file, with the
    /// same spelling and the same case.</summary>
    /// <value>The method name.</value>
    public string MethodName { get; }

    /// <summary>Constructs a Protobuf service method attribute.</summary>
    /// <param name="methodName">The method name.</param>
    public ProtobufServiceMethodAttribute(string methodName) => MethodName = methodName;
}
