// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
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
    /// <param name="field">The tagged field.</param>
    /// <param name="currentNamespace">The current C# namespace.</param>
    /// <param name="paramPrefix">Prefix for the parameter name ("this." for struct fields, "" for operation params).</param>
    /// <param name="encoderName">The name of the encoder variable in the generated code.</param>
    internal static string EncodeTaggedField(
        this Field field,
        string currentNamespace,
        string paramPrefix = "this.",
        string encoderName = "encoder")
    {
        string param = $"{paramPrefix}{field.Name}";
        int tag = field.Tag!.Value;

        string csType = field.DataType.FieldTypeString(false, currentNamespace);
        string varName = $"{field.ParameterName}_";
        string encodeLambda = field.DataType.GetEncodeLambda(false, currentNamespace);

        if (field.DataType.IsValueType)
        {
            string encodeCall = (field.DataType.FixedSize is int fixedSizeValue)
                ? $"{encoderName}.EncodeTagged({tag}, size: {fixedSizeValue}, {varName}, {encodeLambda});"
                : $"{encoderName}.EncodeTagged({tag}, {varName}, {encodeLambda});";
            return new CodeBlock(
                $$"""
                if ({{param}} is {{csType}} {{varName}})
                {
                    {{encodeCall}}
                }
                """).ToString();
        }
        else if (GetCollectionElementSize(field.DataType.Type) is int elemSize)
        {
            string sizeExpr = $"{encoderName}.GetSizeLength(count_) + {elemSize} * count_";
            return new CodeBlock(
                $$"""
                if ({{param}} is {{csType}} {{varName}})
                {
                    int count_ = {{param}}.Count();
                    {{encoderName}}.EncodeTagged({{tag}}, size: {{sizeExpr}}, {{varName}}, {{encodeLambda}});
                }
                """).ToString();
        }
        else
        {
            return new CodeBlock(
                $$"""
                if ({{param}} is {{csType}} {{varName}})
                {
                    {{encoderName}}.EncodeTagged({{tag}}, {{varName}}, {{encodeLambda}});
                }
                """).ToString();
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

    /// <summary>Counts non-tagged optional fields (for Slice bit sequence sizing).</summary>
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

    /// <summary>Generates the encode body for a list of fields. Used by struct, enum-with-fields, and operation
    /// encode methods. The logic is the same: bit sequence for optionals, tagged fields, regular fields, and
    /// an optional tag end marker.</summary>
    /// <param name="fields">The fields to encode.</param>
    /// <param name="currentNamespace">The current C# namespace.</param>
    /// <param name="paramPrefix">Prefix for field access ("this." for struct/enum fields, "" for operation params).</param>
    /// <param name="includeTagEndMarker">Whether to append the Slice tag end marker.</param>
    /// <param name="encoderName">The name of the encoder variable in the generated code.</param>
    internal static CodeBlock GenerateEncodeBody(
        this ImmutableList<Field> fields,
        string currentNamespace,
        string paramPrefix = "this.",
        bool includeTagEndMarker = true,
        string encoderName = "encoder")
    {
        IReadOnlyList<Field> sortedFields = fields.GetSortedFields();
        var body = new CodeBlock();

        int bitSequenceSize = fields.GetBitSequenceSize();
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceWriter = {encoderName}.GetBitSequenceWriter({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            string param = $"{paramPrefix}{field.Name}";

            if (field.IsTagged)
            {
                body.WriteLine(field.EncodeTaggedField(currentNamespace, paramPrefix, encoderName));
            }
            else if (field.DataTypeIsOptional)
            {
                string valueParam = field.DataType.IsValueType ? $"{param}.Value" : param;
                CodeBlock encodeExpr = field.DataType.EncodeExpression(currentNamespace, valueParam, encoderName);
                body.WriteLine($$"""
                    bitSequenceWriter.Write({{param}} != null);
                    if ({{param}} != null)
                    {
                        {{encodeExpr.Indent()}};
                    }
                    """);
            }
            else
            {
                CodeBlock encodeExpr = field.DataType.EncodeExpression(currentNamespace, param, encoderName);
                body.WriteLine($"{encodeExpr};");
            }
        }

        if (includeTagEndMarker)
        {
            body.WriteLine($"{encoderName}.EncodeVarInt32(SliceDefinitions.TagEndMarker);");
        }

        return body;
    }

    extension(Field value)
    {
        /// <summary>Returns true if the streamed field is a raw byte stream (non-optional uint8).</summary>
        internal bool IsByteStream =>
            value.DataType.Type is Builtin b && b.Kind == BuiltinKind.UInt8 && !value.DataTypeIsOptional;

        /// <summary>Gets a value indicating whether this field has the cs::readonly attribute.</summary>
        internal bool IsReadonly => value.Attributes.HasAttribute(CSAttributes.CSReadonly);

        /// <summary>Gets a value indicating whether this field should have the C# 'required' keyword
        /// (non-optional reference type).</summary>
        internal bool IsRequired => !value.DataTypeIsOptional && !value.DataType.IsValueType;
    }
}
