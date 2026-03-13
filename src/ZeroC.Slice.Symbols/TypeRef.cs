// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a reference to a Slice type.
/// </summary>
public record class TypeRef
{
    /// <summary>
    /// Gets the symbol this reference points to.
    /// </summary>
    public required Symbol Symbol { get; init; }

    /// <summary>
    /// Gets a value indicating whether the referenced type is optional.
    /// </summary>
    public required bool IsOptional { get; init; }

    /// <summary>
    /// Gets the list of attributes for the reference.
    /// </summary>
    public required ImmutableList<Attribute> Attributes { get; init; }

    /// <summary>
    /// Gets a value indicating whether the referenced type is a C# value type.
    /// </summary>
    public bool IsValueType => Symbol switch
    {
        Builtin b => b.Kind != BuiltinKind.String,
        Struct => true,
        EnumWithUnderlying e => !e.IsUnchecked,
        _ => false,
    };
}
