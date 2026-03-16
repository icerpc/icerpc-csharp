// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="Field"/>.</summary>
internal static class FieldExtensions
{
    /// <summary>Gets a value indicating whether this field should have the C# 'required' keyword
    /// (non-optional reference type).</summary>
    internal static bool IsRequired(this Field field) => !field.DataTypeIsOptional && !field.DataType.IsValueType();

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
        fields.Count(f => !f.IsTagged && f.DataTypeIsOptional);

    /// <summary>Generates encode statement for a non-tagged field.</summary>
    internal static string EncodeField(this Field field, string currentNamespace) =>
        $"{field.DataType.EncodeExpression(currentNamespace, $"this.{field.Name}")};";


    /// <summary>Generates decode expression for a non-tagged field.</summary>
    internal static string DecodeField(this Field field, string currentNamespace) =>
        field.DataType.DecodeExpression(currentNamespace);

    /// <summary>Generates encode code for a tagged field.</summary>
    internal static string EncodeTaggedField(this Field field, string currentNamespace)
    {
        string param = $"this.{field.Name}";
        int tag = field.Tag!.Value;

        bool isValueType = field.DataType.IsValueType();
        string csType = field.DataType.FieldTypeString(false, currentNamespace);
        string varName = $"{field.ParameterName}_";
        string encodeLambda = field.DataType.GetEncodeLambda(false, currentNamespace);

        if (isValueType)
        {
            int? fixedSize = field.DataType.GetFixedSize();
            string encodeCall = fixedSize.HasValue
                ? $"encoder.EncodeTagged({tag}, size: {fixedSize.Value}, {varName}, {encodeLambda});"
                : $"encoder.EncodeTagged({tag}, {varName}, {encodeLambda});";
            return @$"if ({param} is {csType} {varName})
{{
    {encodeCall}
}}";
        }
        else
        {
            return @$"if ({param} is {csType} {varName})
{{
    encoder.EncodeTagged({tag}, {varName}, {encodeLambda});
}}";
        }
    }

    /// <summary>Gets the full decode expression for a field, handling tagged, optional, and regular fields.</summary>
    internal static string GetFieldDecodeExpression(this Field field, string currentNamespace)
    {
        if (field.IsTagged)
        {
            int tag = field.Tag!.Value;
            string decodeExpr = field.DataType.DecodeExpression(currentNamespace);
            string csType = field.DataType.FieldTypeString(false, currentNamespace);
            return $"decoder.DecodeTagged({tag}, (ref SliceDecoder decoder) => ({csType}?){decodeExpr})";
        }
        else if (field.DataTypeIsOptional)
        {
            string decodeExpr = field.DecodeField(currentNamespace);
            return $"bitSequenceReader.Read() ? {decodeExpr} : null";
        }
        else
        {
            return field.DecodeField(currentNamespace);
        }
    }
}
