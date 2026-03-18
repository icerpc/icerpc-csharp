// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="Field"/>.</summary>
internal static class FieldExtensions
{
    /// <summary>Generates decode expression for a non-tagged field.</summary>
    internal static string DecodeField(this Field field, string currentNamespace) =>
        field.DataType.DecodeExpression(currentNamespace);

    /// <summary>Generates encode statement for a non-tagged field.</summary>
    internal static string EncodeField(this Field field, string currentNamespace) =>
        $"{field.DataType.EncodeExpression(currentNamespace, $"this.{field.Name}")};";


    /// <summary>Generates encode code for a tagged field.</summary>
    internal static string EncodeTaggedField(this Field field, string currentNamespace)
    {
        string param = $"this.{field.Name}";
        int tag = field.Tag!.Value;

        string csType = field.DataType.FieldTypeString(false, currentNamespace);
        string varName = $"{field.ParameterName}_";
        string encodeLambda = field.DataType.GetEncodeLambda(false, currentNamespace);

        if (field.DataType.IsValueType)
        {
            string encodeCall = (field.DataType.FixedSize is int fixedSizeValue)
                ? $"encoder.EncodeTagged({tag}, size: {fixedSizeValue}, {varName}, {encodeLambda});"
                : $"encoder.EncodeTagged({tag}, {varName}, {encodeLambda});";
            return @$"if ({param} is {csType} {varName})
{{
    {encodeCall}
}}";
        }
        else if (GetCollectionElementSize(field.DataType.Type) is int elemSize)
        {
            string sizeExpr = $"encoder.GetSizeLength(count_) + {elemSize} * count_";
            return @$"if ({param} is {csType} {varName})
{{
    int count_ = {param}.Count();
    encoder.EncodeTagged({tag}, size: {sizeExpr}, {varName}, {encodeLambda});
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

    /// <summary>Returns the per-entry fixed wire size for a collection type, or null if variable-size.
    /// For sequences, returns the element's fixed size (excluding size-1 elements which use an optimized
    /// raw-bytes encoding path). For dictionaries, returns the sum of key and value fixed sizes.</summary>
    private static int? GetCollectionElementSize(IType type) => type switch
    {
        SequenceType seq => seq.ElementType.FixedSize is > 1 ? seq.ElementType.FixedSize : null,
        DictionaryType dict => dict.KeyType.FixedSize is int k && dict.ValueType.FixedSize is int v
            ? k + v : null,
        _ => null,
    };

    /// <summary>Counts non-tagged optional fields (for Slice2 bit sequence sizing).</summary>
    internal static int GetBitSequenceSize(this ImmutableList<Field> fields) =>
        fields.Count(f => !f.IsTagged && f.DataTypeIsOptional);

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

    /// <summary>Returns fields sorted: non-tagged in original order, then tagged sorted by tag value.</summary>
    internal static IReadOnlyList<Field> GetSortedFields(this ImmutableList<Field> fields)
    {
        var nonTagged = fields.Where(f => !f.IsTagged).ToList();
        var tagged = fields.Where(f => f.IsTagged).OrderBy(f => f.Tag!.Value).ToList();
        nonTagged.AddRange(tagged);
        return nonTagged;
    }

    extension(Field value)
    {
        /// <summary>Gets a value indicating whether this field has the cs::readonly attribute.</summary>
        internal bool IsReadonly => value.Attributes.HasAttribute(CSAttributes.CSReadonly);

        /// <summary>Gets a value indicating whether this field should have the C# 'required' keyword
        /// (non-optional reference type).</summary>
        internal bool IsRequired => !value.DataTypeIsOptional && !value.DataType.IsValueType;
    }
}
