// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Protobuf.RpcMethods;

/// <summary>An attribute that IceRpc.Protobuf.Generator applies to abstract methods it generates on server-side
/// interfaces (I{Name}Service). This attribute communicates information about the Protobuf rpc method to the Service
/// Generator (IceRpc.ServiceGenerator): currently only the name of the rpc method. The Service Generator generates code
/// that matches the operation name in incoming requests to the method name specified by this attribute.
/// </summary>
/// <remarks>We limit the information communicated through this attribute to the strict minimum. The Service Generator
/// can deduce most information from the signature of the method decorated with this attribute, such as the method
/// kind (unary, client streaming, server streaming, or bidi streaming).</remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RpcMethodAttribute : Attribute
{
    /// <summary>Gets the method name. It corresponds to the name of the rpc method in the Protobuf file, with the
    /// same spelling and the same case.</summary>
    /// <value>The method name.</value>
    public string Name { get; }

    /// <summary>Constructs an RPC method attribute.</summary>
    /// <param name="name">The method name.</param>
    public RpcMethodAttribute(string name) => Name = name;
}
