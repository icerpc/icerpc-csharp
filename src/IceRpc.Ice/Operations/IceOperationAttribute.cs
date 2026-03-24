// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Ice.Operations;

/// <summary>An attribute that Ice's Slice compiler (slice2cs) applies to abstract methods it generates on server-side
/// interfaces (I{Name}Service). This attribute communicates information about the Ice operation to the IceRPC Service
/// Generator (IceRpc.ServiceGenerator): the name of the operation plus various optional properties (see below).
/// The Service Generator generates code that matches the operation name in incoming requests to the operation name
/// specified by this attribute.</summary>
/// <remarks>We limit the information communicated through this attribute to the strict minimum. The Service Generator
/// can deduce most information from the signature of the method decorated with this attribute, such as the number of
/// parameters and their mapped types.</remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class IceOperationAttribute : Attribute
{
    /// <summary>Gets the operation name.</summary>
    /// <value>The operation name.</value>
    public string Name { get; }

    /// <summary>Gets the exception specification of the operation.</summary>
    /// <value>An array of Ice exception types that the operation may throw.</value>
    /// <seealso cref="IceException"/>
    public Type[] ExceptionSpecification { get; init; } = [];

    /// <summary>Gets a value indicating whether the operation is idempotent.</summary>
    /// <value><see langword="true"/> if the operation is idempotent; otherwise, <see langword="false"/>.</value>
    public bool Idempotent { get; init; }

    /// <summary>Constructs an Ice operation attribute.</summary>
    /// <param name="name">The operation name.</param>
    public IceOperationAttribute(string name) => Name = name;
}
