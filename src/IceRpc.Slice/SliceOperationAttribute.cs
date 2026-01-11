// Copyright (c) ZeroC, Inc.

using System.ComponentModel;
using ZeroC.Slice;

namespace IceRpc.Slice;

/// <summary>Represents an attribute that the Slice compiler uses to mark helper methods it generates on Service
/// interfaces.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SliceOperationAttribute : Attribute
{
    /// <summary>Gets the operation name.</summary>
    /// <value>The operation name.</value>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the return value needs to be compressed.</summary>
    /// <value><see langword="true"/> if the return values needs to be compressed; otherwise, <see langword="false"/>.
    /// </value>
    public bool CompressReturnValue { get; init; }

    /// <summary>Gets the encoding used to encode the request and response payloads of this operation.</summary>
    /// <value>The Slice encoding.</value>
    public SliceEncoding Encoding { get; init; } = SliceEncoding.Slice2;

    /// <summary>Gets the exception specification of the operation.</summary>
    /// <value>An array of Slice exception types that the operation may throw.</value>
    /// <remarks>Slice1-only.</remarks>
    /// <seealso cref="SliceException"/>
    public Type[] ExceptionSpecification { get; init; } = [];

    /// <summary>Gets a value indicating whether the operation is idempotent.</summary>
    /// <value><see langword="true"/> if the operation is idempotent; otherwise, <see langword="false"/>.</value>
    public bool Idempotent { get; init; }

    /// <summary>Constructs a Slice operation attribute.</summary>
    /// <param name="name">The operation name.</param>>
    public SliceOperationAttribute(string name) => Name = name;
}
