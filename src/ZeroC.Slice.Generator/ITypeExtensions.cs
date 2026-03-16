// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for <see cref="IType"/>.</summary>
internal static class ITypeExtensions
{
    /// <summary>Generates encode expression for a type (without trailing semicolon).</summary>
    internal static string EncodeExpression(this IType type, string currentNamespace, string param)
    {
        return type switch
        {
            Builtin builtin => $"encoder.Encode{builtin.Suffix()}({param})",
            SequenceType seq => EncodeSequence(seq, currentNamespace, param),
            DictionaryType dict => EncodeDictionary(dict, currentNamespace, param),
            EnumWithUnderlying e when e.IsUnchecked =>
                $"encoder.Encode{e.Underlying.Suffix()}(({e.Underlying.CsType()}){param})",
            EnumWithUnderlying e =>
                $"{e.EncoderExtensionsClass}.Encode{e.Name}(ref encoder, {param})",
            EnumWithFields e => $"{e.EncoderExtensionsClass}.Encode{e.Name}(ref encoder, {param})",
            CustomType c => $"{c.EncoderExtensionsClass}.Encode{c.Name}(ref encoder, {param})",
            ResultType result => EncodeResult(result, currentNamespace, param),
            _ => $"{param}.Encode(ref encoder)",
        };

        static string EncodeSequence(SequenceType seq, string currentNamespace, string param)
        {
            IType elemType = seq.ElementType.Type;
            if (seq.ElementTypeIsOptional && (seq.ElementType.IsValueType() || elemType is CustomType))
            {
                string csOptType = seq.ElementType.FieldTypeString(true, currentNamespace);
                string lambda = EncodeOptionalValueLambda(elemType, csOptType);
                return $"encoder.EncodeSequenceOfOptionals({param}, {lambda})";
            }
            string elementEncodeLambda = elemType.GetEncodeLambda(seq.ElementTypeIsOptional, currentNamespace);
            return $"encoder.EncodeSequence({param}, {elementEncodeLambda})";

            static string EncodeOptionalValueLambda(IType elemType, string csOptType)
            {
                if (elemType is Entity entity and (EnumWithUnderlying or EnumWithFields or CustomType))
                {
                    string extClass = entity.EncoderExtensionsClass;
                    string name = entity.Name;
                    return $"(ref SliceEncoder encoder, {csOptType} value) => {extClass}.Encode{name}(ref encoder, (value ?? default!))";
                }
                return $"(ref SliceEncoder encoder, {csOptType} value) => (value ?? default!).Encode(ref encoder)";
            }
        }

        static string EncodeDictionary(DictionaryType dict, string currentNamespace, string param)
        {
            string keyEncodeLambda = dict.KeyType.Type.GetEncodeLambda(false, currentNamespace);
            string valueEncodeLambda = dict.ValueType.Type.GetEncodeLambda(dict.ValueTypeIsOptional, currentNamespace);
            return $"encoder.EncodeDictionary({param}, {keyEncodeLambda}, {valueEncodeLambda})";
        }

        static string EncodeResult(ResultType result, string currentNamespace, string param)
        {
            string encodeLambda = ResultEncodeLambda(result.SuccessType, result.SuccessTypeIsOptional, currentNamespace);
            string encodeFailureLambda = ResultEncodeLambda(result.FailureType, result.FailureTypeIsOptional, currentNamespace);
            return $"encoder.EncodeResult({param}, {encodeLambda}, {encodeFailureLambda})";
        }

        // Returns an encode lambda for a result success/failure type, handling optional inner types with an
        // inline bool marker (encoder.EncodeBool()) rather than the bit-sequence pattern used for struct fields.
        static string ResultEncodeLambda(TypeRef typeRef, bool isOptional, string currentNamespace)
        {
            IType type = typeRef.Type;

            if (!isOptional)
            {
                return type.GetEncodeLambda(false, currentNamespace);
            }
            string csType = typeRef.FieldTypeString(true, currentNamespace);
            string valueParam = typeRef.IsValueType() ? "value!.Value" : "value!";
            string encodeBody = type.EncodeExpression(currentNamespace, valueParam);
            return $$"""
                (ref SliceEncoder encoder, {{csType}} value) =>
                {
                    encoder.EncodeBool(value is not null);
                    if (value is not null)
                    {
                        {{encodeBody}};
                    }
                }
                """;
        }
    }

    /// <summary>Generates decode expression for a type.</summary>
    internal static string DecodeExpression(this IType type, string currentNamespace)
    {
        return type switch
        {
            Builtin builtin => $"decoder.Decode{builtin.Suffix()}()",
            SequenceType seq => DecodeSequence(seq, currentNamespace),
            DictionaryType dict => DecodeDictionary(dict, currentNamespace),
            EnumWithUnderlying e when e.IsUnchecked =>
                $"({e.ToTypeString(currentNamespace)})decoder.Decode{e.Underlying.Suffix()}()",
            EnumWithUnderlying e =>
                $"{e.DecoderExtensionsClass}.Decode{e.Name}(ref decoder)",
            EnumWithFields e => $"{e.DecoderExtensionsClass}.Decode{e.Name}(ref decoder)",
            CustomType c => $"{c.DecoderExtensionsClass}.Decode{c.Name}(ref decoder)",
            ResultType r => DecodeResult(r, currentNamespace),
            _ => $"new {type.ToTypeString(currentNamespace)}(ref decoder)",
        };

        static string DecodeSequence(SequenceType seq, string currentNamespace)
        {
            IType elemType = seq.ElementType.Type;
            if (seq.ElementTypeIsOptional && (seq.ElementType.IsValueType() || elemType is CustomType))
            {
                string baseType = elemType.ToTypeString(currentNamespace);
                string decodeExpr = elemType.DecodeExpression(currentNamespace);
                return $"decoder.DecodeSequenceOfOptionals((ref SliceDecoder decoder) => ({baseType}?){decodeExpr})";
            }
            string elementDecodeLambda = elemType.GetDecodeLambda(seq.ElementTypeIsOptional, currentNamespace);
            return $"decoder.DecodeSequence({elementDecodeLambda})";
        }

        static string DecodeDictionary(DictionaryType dict, string currentNamespace)
        {
            string keyDecodeLambda = dict.KeyType.Type.GetDecodeLambda(false, currentNamespace);
            string valueDecodeLambda = dict.ValueType.Type.GetDecodeLambda(
                dict.ValueTypeIsOptional,
                currentNamespace);
            return $"decoder.DecodeDictionary({keyDecodeLambda}, {valueDecodeLambda})";
        }

        static string DecodeResult(ResultType result, string currentNamespace)
        {
            string decodeLambda = ResultDecodeLambda(
                result.SuccessType,
                result.SuccessTypeIsOptional,
                currentNamespace);
            string decodeFailureLambda = ResultDecodeLambda(
                result.FailureType,
                result.FailureTypeIsOptional,
                currentNamespace);
            return $"decoder.DecodeResult({decodeLambda}, {decodeFailureLambda})";
        }

        // Returns a decode lambda for a result success/failure type, handling optional inner types with an
        // inline bool marker (decoder.DecodeBool()) rather than the bit-sequence pattern used for struct fields.
        static string ResultDecodeLambda(TypeRef typeRef, bool isOptional, string currentNamespace)
        {
            IType type = typeRef.Type;

            if (!isOptional)
            {
                return type.GetDecodeLambda(false, currentNamespace);
            }
            string csType = type.ToTypeString(currentNamespace);
            string decodeExpr = type.DecodeExpression(currentNamespace);
            return $"(ref SliceDecoder decoder) => decoder.DecodeBool() ? ({csType}?){decodeExpr} : null";
        }
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

    /// <summary>Returns a decode lambda for a type.</summary>
    internal static string GetDecodeLambda(this IType type, bool isOptional, string currentNamespace)
    {
        string decodeExpr = type.DecodeExpression(currentNamespace);
        if (type is not Builtin && isOptional)
        {
            string csType = type.ToTypeString(currentNamespace);
            return $"(ref SliceDecoder decoder) => ({csType}?){decodeExpr}";
        }
        return $"(ref SliceDecoder decoder) => {decodeExpr}";
    }

    /// <summary>Returns the C# type string for a type (without optional modifier).</summary>
    internal static string ToTypeString(this IType type, string currentNamespace)
    {
        return type switch
        {
            Builtin builtin => builtin.CsType(),
            SequenceType seq => SequenceToTypeString(seq, currentNamespace),
            DictionaryType dict => DictionaryToTypeString(dict, currentNamespace),
            ResultType result => ResultToTypeString(result, currentNamespace),
            CustomType c => CustomToTypeString(c),
            Entity entity => EntityToTypeString(entity, currentNamespace),
            _ => throw new InvalidOperationException($"Unexpected type '{type.GetType().Name}'."),
        };

        static string SequenceToTypeString(SequenceType seq, string currentNamespace)
        {
            string elementType = seq.ElementType.FieldTypeString(seq.ElementTypeIsOptional, currentNamespace);
            return $"global::System.Collections.Generic.IList<{elementType}>";
        }

        static string DictionaryToTypeString(DictionaryType dict, string currentNamespace)
        {
            string keyType = dict.KeyType.FieldTypeString(false, currentNamespace);
            string valueType = dict.ValueType.FieldTypeString(dict.ValueTypeIsOptional, currentNamespace);
            return $"global::System.Collections.Generic.IDictionary<{keyType}, {valueType}>";
        }

        static string ResultToTypeString(ResultType result, string currentNamespace)
        {
            string successType = result.SuccessType.FieldTypeString(result.SuccessTypeIsOptional, currentNamespace);
            string failureType = result.FailureType.FieldTypeString(result.FailureTypeIsOptional, currentNamespace);
            return $"Result<{successType}, {failureType}>";
        }

        static string CustomToTypeString(CustomType c)
        {
            // TODO: we should validate all CustomType definitions have the required cs::type attribute before we
            // generate any code.
            Symbols.Attribute csTypeAttr = c.Attributes.FindAttribute(CsAttributes.CsType)!.Value;
            return csTypeAttr.Args[0];
        }

        static string EntityToTypeString(Entity entity, string currentNamespace) =>
            currentNamespace == entity.Namespace
                ? entity.Name
                : $"global::{entity.Namespace}.{entity.Name}";
    }

}
