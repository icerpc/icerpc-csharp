// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="Field"/>.</summary>
internal static class FieldExtensions
{
    /// <summary>Gets a value indicating whether this field should have the C# 'required' keyword
    /// (non-optional reference type).</summary>
    internal static bool IsRequired(this Field field) => !field.IsOptional && !field.Type.IsValueType();

    /// <summary>Returns fields sorted: non-tagged in original order, then tagged sorted by tag value.</summary>
    internal static IReadOnlyList<Field> GetSortedFields(this ImmutableList<Field> fields)
    {
        var nonTagged = fields.Where(f => !f.IsTagged).ToList();
        var tagged = fields.Where(f => f.IsTagged).OrderBy(f => f.Tag!.Value).ToList();
        nonTagged.AddRange(tagged);
        return nonTagged;
    }

    /// <summary>Counts non-tagged optional fields (for Slice2 bit sequence sizing).</summary>
    internal static int GetBitSequenceSize(this ImmutableList<Field> fields) =>
        fields.Count(f => !f.IsTagged && f.IsOptional);
}
