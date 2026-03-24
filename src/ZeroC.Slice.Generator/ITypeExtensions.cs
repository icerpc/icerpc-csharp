// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for <see cref="IType"/>.</summary>
internal static class ITypeExtensions
{
    /// <summary>Generates decode expression for a type.</summary>
    /// <param name="type">The type to decode.</param>
    /// <param name="currentNamespace">The current C# namespace for resolving type names.</param>
    /// <param name="concreteType">Optional concrete type override from a cs::type attribute on the TypeRef.
    /// Used as the factory type for dictionary/sequence decoding instead of the default.</param>
    internal static string DecodeExpression(this IType type, string currentNamespace, string? concreteType = null)
    {
        return type switch
        {
            Builtin builtin => $"decoder.Decode{builtin.Suffix}()",
            DictionaryType dict => DecodeDictionary(dict, currentNamespace, concreteType),
            BasicEnum e when e.IsUnchecked =>
                $"({e.ToTypeString(currentNamespace)})decoder.Decode{e.Underlying.Suffix}()",
            Entity e when e.UsesExtensionsClass =>
                $"{e.DecoderExtensionsClass}.Decode{e.Name}(ref decoder)",
            ResultType r => DecodeResult(r, currentNamespace),
            SequenceType seq => DecodeSequence(seq, currentNamespace, concreteType),
            _ => $"new {type.ToTypeString(currentNamespace)}(ref decoder)",
        };

        static string DecodeDictionary(DictionaryType dict, string currentNamespace, string? concreteType)
        {
            CodeBlock keyDecodeLambda = dict.KeyType.Type.GetDecodeLambda(false, currentNamespace);
            CodeBlock valueDecodeLambda = dict.ValueType.Type.GetDecodeLambda(
                dict.ValueTypeIsOptional,
                currentNamespace,
                withCast: true);
            string keyType = dict.KeyType.FieldTypeString(false, currentNamespace);
            string valueType = dict.ValueType.FieldTypeString(dict.ValueTypeIsOptional, currentNamespace);
            string concreteDictType = concreteType
                ?? $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
            string method = dict.ValueTypeIsOptional
                ? "DecodeDictionaryWithOptionalValueType"
                : "DecodeDictionary";
            return $$"""
                decoder.{{method}}(
                    size => new {{concreteDictType}}(size),
                    {{keyDecodeLambda.Indent()}},
                    {{valueDecodeLambda.Indent()}})
                """;
        }

        static string DecodeResult(ResultType result, string currentNamespace)
        {
            CodeBlock decodeLambda = ResultDecodeLambda(
                result.SuccessType,
                result.SuccessTypeIsOptional,
                currentNamespace);
            CodeBlock decodeFailureLambda = ResultDecodeLambda(
                result.FailureType,
                result.FailureTypeIsOptional,
                currentNamespace);
            return $$"""
                decoder.DecodeResult(
                    {{decodeLambda.Indent()}}, 
                    {{decodeFailureLambda.Indent()}})
                """;
        }

        static string DecodeSequence(SequenceType seq, string currentNamespace, string? concreteType)
        {
            IType elemType = seq.ElementType.Type;
            string method = seq.ElementTypeIsOptional
                ? "decoder.DecodeSequenceOfOptionals"
                : "decoder.DecodeSequence";
            CodeBlock decodeLambda = elemType.GetDecodeLambda(seq.ElementTypeIsOptional, currentNamespace);

            if (concreteType is not null)
            {
                string factory = $"sequenceFactory: (size) => new {concreteType}(size)";
                return $$"""
                    {{method}}(
                        {{factory}},
                        {{decodeLambda.Indent()}})
                    """;
            }

            // For nested sequences, cast the result so C# can convert T[][] to IList<T>[].
            string nestedCast = elemType is SequenceType
                ? $"({seq.ElementType.FieldTypeString(seq.ElementTypeIsOptional, currentNamespace)}[])"
                : "";

            return $$"""
                {{nestedCast}}{{method}}(
                    {{decodeLambda.Indent()}})
                """;
        }

        // Returns a decode lambda for a result success/failure type, handling optional inner types with an
        // inline bool marker (decoder.DecodeBool()) rather than the bit-sequence pattern used for struct fields.
        static string ResultDecodeLambda(TypeRef typeRef, bool isOptional, string currentNamespace)
        {
            IType type = typeRef.Type;

            if (!isOptional)
            {
                return type.GetDecodeLambda(false, currentNamespace, withCast: true);
            }
            string csType = type.ToTypeString(currentNamespace);
            string decodeExpr = type.DecodeExpression(currentNamespace);
            return $"(ref SliceDecoder decoder) => decoder.DecodeBool() ? ({csType}?){decodeExpr} : null";
        }
    }

    /// <summary>Generates encode expression for a type (without trailing semicolon).</summary>
    internal static string EncodeExpression(
        this IType type,
        string currentNamespace,
        string param,
        string encoderName = "encoder")
    {
        return type switch
        {
            Builtin builtin => $"{encoderName}.Encode{builtin.Suffix}({param})",
            DictionaryType dict => EncodeDictionary(dict, currentNamespace, param, encoderName),
            BasicEnum e when e.IsUnchecked =>
                $"{encoderName}.Encode{e.Underlying.Suffix}(({e.Underlying.CSType}){param})",
            Entity e when e.UsesExtensionsClass =>
                $"{e.EncoderExtensionsClass}.Encode{e.Name}(ref {encoderName}, {param})",
            SequenceType seq => EncodeSequence(seq, currentNamespace, param, encoderName),
            ResultType result => EncodeResult(result, currentNamespace, param, encoderName),
            _ => $"{param}.Encode(ref {encoderName})",
        };

        static string EncodeDictionary(
            DictionaryType dict,
            string currentNamespace,
            string param,
            string encoderName)
        {
            CodeBlock keyEncodeLambda = dict.KeyType.GetEncodeLambda(false, currentNamespace);
            CodeBlock valueEncodeLambda = dict.ValueType.GetEncodeLambda(dict.ValueTypeIsOptional, currentNamespace);
            string method = dict.ValueTypeIsOptional
                ? "EncodeDictionaryWithOptionalValueType"
                : "EncodeDictionary";
            return $$"""
                {{encoderName}}.{{method}}(
                    {{param}},
                    {{keyEncodeLambda.Indent()}},
                    {{valueEncodeLambda.Indent()}})
                """;
        }

        static string EncodeResult(
            ResultType result,
            string currentNamespace,
            string param,
            string encoderName)
        {
            CodeBlock encodeLambda = ResultEncodeLambda(result.SuccessType, result.SuccessTypeIsOptional, currentNamespace);
            CodeBlock encodeFailureLambda = ResultEncodeLambda(result.FailureType, result.FailureTypeIsOptional, currentNamespace);
            return $$"""
                {{encoderName}}.EncodeResult(
                    {{param}},
                    {{encodeLambda.Indent()}},
                    {{encodeFailureLambda.Indent()}})
                """;
        }

        static string EncodeSequence(
            SequenceType seq,
            string currentNamespace,
            string param,
            string encoderName)
        {
            IType elemType = seq.ElementType.Type;
            if (seq.ElementTypeIsOptional && (seq.ElementType.IsValueType || elemType is CustomType))
            {
                string csOptType = seq.ElementType.FieldTypeString(true, currentNamespace);
                string lambda = EncodeOptionalValueLambda(elemType, csOptType);
                return $$"""
                    {{encoderName}}.EncodeSequenceOfOptionals(
                        {{param}},
                        {{lambda}})
                    """;
            }
            CodeBlock elementEncodeLambda = seq.ElementType.GetEncodeLambda(seq.ElementTypeIsOptional, currentNamespace);
            return $$"""
                {{encoderName}}.EncodeSequence(
                    {{param}},
                    {{elementEncodeLambda.Indent()}})
                """;

            static string EncodeOptionalValueLambda(IType elemType, string csOptType)
            {
                // CustomType → (value ?? default!), value types → value!.Value, reference types → value!
                string valueExpr = elemType is CustomType
                    ? "(value ?? default!)"
                    : elemType is Struct or BasicEnum ? "value!.Value" : "value!";
                if (elemType is Entity entity && entity.UsesExtensionsClass)
                {
                    string extClass = entity.EncoderExtensionsClass;
                    string name = entity.Name;
                    return $"(ref SliceEncoder encoder, {csOptType} value) => {extClass}.Encode{name}(ref encoder, {valueExpr})";
                }
                return $"(ref SliceEncoder encoder, {csOptType} value) => {valueExpr}.Encode(ref encoder)";
            }
        }

        // Returns an encode lambda for a result success/failure type, handling optional inner types with an
        // inline bool marker (encoder.EncodeBool()) rather than the bit-sequence pattern used for struct fields.
        static string ResultEncodeLambda(TypeRef typeRef, bool isOptional, string currentNamespace)
        {
            IType type = typeRef.Type;

            if (!isOptional)
            {
                return typeRef.GetEncodeLambda(false, currentNamespace);
            }
            string csType = typeRef.FieldTypeString(true, currentNamespace);
            string valueParam = typeRef.IsValueType ? "value!.Value" : "value!";
            CodeBlock encodeBody = type.EncodeExpression(currentNamespace, valueParam);
            return $$"""
                (ref SliceEncoder encoder, {{csType}} value) =>
                {
                    encoder.EncodeBool(value is not null);
                    if (value is not null)
                    {
                        {{encodeBody.Indent().Indent()}};
                    }
                }
                """;
        }
    }

    /// <summary>Returns a decode lambda for a type. When <paramref name="withCast"/> is true, a cast to the field
    /// type is added for sequence and dictionary types. This is needed when decoding in a generic context (e.g.,
    /// dictionary values, result types) where C# cannot implicitly convert nested generic types.</summary>
    internal static string GetDecodeLambda(
        this IType type,
        bool isOptional,
        string currentNamespace,
        bool withCast = false)
    {
        string decodeExpr = type.DecodeExpression(currentNamespace);

        // For dict/seq in generic contexts (withCast), use a single combined cast that includes ? if optional.
        // Without withCast, dict/seq get no cast here — the caller handles it (e.g., nested sequence cast).
        if (withCast && type is DictionaryType or SequenceType)
        {
            string csType = type.ToTypeString(currentNamespace);
            string cast = isOptional ? $"({csType}?)" : $"({csType})";
            return $"(ref SliceDecoder decoder) => {cast}{decodeExpr}";
        }

        // For non-dict/non-seq optional types, add the nullable cast.
        if (isOptional && type is not DictionaryType and not SequenceType)
        {
            string csType = type.ToTypeString(currentNamespace);
            return $"(ref SliceDecoder decoder) => ({csType}?){decodeExpr}";
        }

        return $"(ref SliceDecoder decoder) => {decodeExpr}";
    }

    /// <summary>Returns an encode lambda for a type.</summary>
    internal static string GetEncodeLambda(this IType type, bool isOptional, string currentNamespace)
    {
        string csType = type.ToTypeString(currentNamespace);
        if (isOptional)
        {
            csType += "?";
        }
        string encodeExpr = type.EncodeExpression(currentNamespace, "value");
        return $"(ref SliceEncoder encoder, {csType} value) => {encodeExpr}";
    }

    /// <summary>Returns the C# type string for a type (without optional modifier).</summary>
    internal static string ToTypeString(this IType type, string currentNamespace)
    {
        return type switch
        {
            Builtin builtin => builtin.CSType,
            CustomType c => CustomToTypeString(c),
            DictionaryType dict => DictionaryToTypeString(dict, currentNamespace),
            Entity entity => EntityToTypeString(entity, currentNamespace),
            ResultType result => ResultToTypeString(result, currentNamespace),
            SequenceType seq => SequenceToTypeString(seq, currentNamespace),
            _ => throw new InvalidOperationException($"Unexpected type '{type.GetType().Name}'."),
        };

        static string CustomToTypeString(CustomType c)
        {
            // The attribute validator ensures custom types has a cs::type attribute.
            Symbols.Attribute csTypeAttr = c.Attributes.FindAttribute(CSAttributes.CSType)!.Value;
            return csTypeAttr.Args[0];
        }

        static string DictionaryToTypeString(DictionaryType dict, string currentNamespace)
        {
            string keyType = dict.KeyType.FieldTypeString(false, currentNamespace);
            string valueType = dict.ValueType.FieldTypeString(dict.ValueTypeIsOptional, currentNamespace);
            return $"global::System.Collections.Generic.IDictionary<{keyType}, {valueType}>";
        }

        static string EntityToTypeString(Entity entity, string currentNamespace) =>
            currentNamespace == entity.Namespace
                ? entity.Name
                : $"global::{entity.Namespace}.{entity.Name}";

        static string ResultToTypeString(ResultType result, string currentNamespace)
        {
            string successType = result.SuccessType.FieldTypeString(result.SuccessTypeIsOptional, currentNamespace);
            string failureType = result.FailureType.FieldTypeString(result.FailureTypeIsOptional, currentNamespace);
            return $"Result<{successType}, {failureType}>";
        }

        static string SequenceToTypeString(SequenceType seq, string currentNamespace)
        {
            string elementType = seq.ElementType.FieldTypeString(seq.ElementTypeIsOptional, currentNamespace);
            return $"global::System.Collections.Generic.IList<{elementType}>";
        }
    }
}
