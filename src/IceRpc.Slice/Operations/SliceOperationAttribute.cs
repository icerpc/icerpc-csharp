// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Slice.Operations;

/// <summary>An attribute that IceRpc.Slice.Generator applies to abstract methods it generates on server-side
/// interfaces (I{Name}Service). This attribute communicates information about the Slice operation to the IceRPC
/// Service Generator (IceRpc.ServiceGenerator): the name of the Slice operation plus various optional properties
/// (see below). The Service Generator generates code that matches the operation name in incoming requests to the
/// operation name specified by this attribute.</summary>
/// <remarks>We limit the information communicated through this attribute to the strict minimum. The Service Generator
/// can deduce most information from the signature of the method decorated with this attribute, such as the number of
/// parameters and their mapped types.</remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SliceOperationAttribute : Attribute
{
    /// <summary>Gets the operation name.</summary>
    /// <value>The operation name.</value>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the return value needs to be compressed.</summary>
    /// <value><see langword="true"/> if the return value needs to be compressed; otherwise, <see langword="false"/>.
    /// </value>
    public bool CompressReturn { get; init; }

    /// <summary>Gets a value indicating whether the non-stream portion of the return value is pre-encoded by the
    /// application.</summary>
    /// <value><see langword="true"/> if the non-stream portion of the return value is pre-encoded; otherwise,
    /// <see langword="false"/>.</value>
    public bool EncodedReturn { get; init; }

    /// <summary>Gets a value indicating whether the operation is idempotent.</summary>
    /// <value><see langword="true"/> if the operation is idempotent; otherwise, <see langword="false"/>.</value>
    public bool Idempotent { get; init; }

    /// <summary>Constructs a Slice operation attribute.</summary>
    /// <param name="name">The operation name.</param>
    public SliceOperationAttribute(string name) => Name = name;
}
