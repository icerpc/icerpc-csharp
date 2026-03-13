// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a field in a Slice definition.
/// Slice fields are used in various contexts, such as structs, enumerations, operation parameters, and return values.
/// </summary>
public record class Field
{
    /// <summary>
    /// Gets the field's entity information.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }

    /// <summary>
    /// Gets the tag for this field, it is <see langword="null"/> for non tagged fields.
    /// </summary>
    public required int? Tag { get; init; }

    /// <summary>
    /// Gets the field's type.
    /// </summary>
    public required TypeRef Type { get; init; }

    /// <summary>
    /// Gets a value indicating whether this field is tagged. When <see langword="true"/>, the <see cref="Tag"/>
    /// property has a value; otherwise, it is <see langword="null"/>.
    /// </summary>
    public bool IsTagged => Tag.HasValue;

    /// <summary>
    /// Gets a value indicating whether this field should have the 'required' keyword (non-optional reference type).
    /// </summary>
    public bool IsRequired => !Type.IsOptional && !Type.IsValueType;
}
