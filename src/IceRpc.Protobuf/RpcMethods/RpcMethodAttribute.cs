// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Protobuf.RpcMethods;

/// <summary>An attribute that IceRPC's protoc plugin applies to abstract methods it generates on server-side
/// interfaces (XxxService). This attribute communicates information about the Protobuf rpc method to the Service
/// generator (IceRpc.ServiceGenerator); for <c>RpcMethodAttribute</c>, it's the name of the rpc method, that the
/// Service generator maps as-is to the operation name carried by the request.</summary>
/// <remarks>We limit the information communicated through this attribute to the strict minimum. The Service generator
/// can deduct most information from the signature of the method decorated with this attribute, such as the method
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
