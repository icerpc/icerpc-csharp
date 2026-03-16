// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Abstract base class for code generators.</summary>
internal class Generator
{
    /// <summary>Qualifies a type name relative to the current namespace.</summary>
    protected static string ScopedIdentifier(string identifier, string identifierNamespace, string currentNamespace) =>
        currentNamespace == identifierNamespace
            ? identifier
            : $"global::{identifierNamespace}.{identifier}";

    /// <summary>Resolves a type to its C# type string.</summary>
    protected string TypeString(IType type, bool isOptional, string currentNamespace)
    {
        string baseType = ResolveBaseType(type, currentNamespace);
        return isOptional ? $"{baseType}?" : baseType;
    }

    /// <summary>Generates encode code for a non-tagged field.</summary>
    protected string EncodeField(Field field, string currentNamespace) =>
        EncodeExpression(field.Type.Type, currentNamespace, $"this.{field.Name}");

    /// <summary>Generates decode expression for a non-tagged field.</summary>
    protected string DecodeField(Field field, string currentNamespace) =>
        DecodeExpression(field.Type.Type, currentNamespace);

    /// <summary>Generates encode code for a tagged field.</summary>
    protected string EncodeTaggedField(Field field, string currentNamespace)
    {
        string param = $"this.{field.Name}";
        int tag = field.Tag!.Value;

        bool isValueType = field.Type.IsValueType();
        string csType = ResolveBaseType(field.Type.Type, currentNamespace);
        string varName = $"{field.ParameterName}_";
        string encodeLambda = GetEncodeLambda(field.Type.Type, false, currentNamespace);

        if (isValueType)
        {
            int? fixedSize = GetFixedSize(field.Type.Type);
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
    protected string GetFieldDecodeExpression(Field field, string currentNamespace)
    {
        if (field.IsTagged)
        {
            int tag = field.Tag!.Value;
            string decodeExpr = DecodeExpression(field.Type.Type, currentNamespace);
            string csType = ResolveBaseType(field.Type.Type, currentNamespace);
            return $"decoder.DecodeTagged({tag}, (ref SliceDecoder decoder) => ({csType}?){decodeExpr})";
        }
        else if (field.IsOptional)
        {
            string decodeExpr = DecodeField(field, currentNamespace);
            return $"bitSequenceReader.Read() ? {decodeExpr} : null";
        }
        else
        {
            return DecodeField(field, currentNamespace);
        }
    }

    /// <summary>Generates a property declaration for a field.</summary>
    protected CodeBlock FieldDeclaration(
        Field field,
        string currentNamespace,
        string accessModifier,
        bool parentReadonly)
    {
        var code = new CodeBlock();

        // cs::attribute
        code.WriteCsAttributes(field.Attributes);

        string typeString = TypeString(field.Type.Type, field.IsOptional, currentNamespace);
        string fieldName = field.Name;
        string required = field.IsRequired() ? "required " : "";
        bool fieldReadonly = field.Attributes.HasAttribute(CsAttributes.CsReadonly);
        string accessor = (parentReadonly || fieldReadonly) ? "{ get; init; }" : "{ get; set; }";

        code.WriteLine($"{accessModifier} {required}{typeString} {fieldName} {accessor}");

        return code;
    }

    protected string EncodeExpression(IType type, string currentNamespace, string param) =>
        type switch
        {
            Builtin builtin => $"encoder.Encode{builtin.Suffix()}({param});",
            SequenceType seq => EncodeSequence(seq, currentNamespace, param),
            DictionaryType dict => EncodeDictionary(dict, currentNamespace, param),
            EnumWithUnderlying e when !e.IsUnchecked =>
                $"{GetEncoderExtensionsClass(e)}.Encode{e.Name}(ref encoder, {param});",
            EnumWithFields e =>
                $"{GetEncoderExtensionsClass(e)}.Encode{e.Name}(ref encoder, {param});",
            CustomType c =>
                $"{GetEncoderExtensionsClass(c)}.Encode{c.Name}(ref encoder, {param});",
            ResultType r =>
                $"encoder.EncodeResult({param}, {GetResultEncodeLambda(r, currentNamespace)}, {GetResultEncodeLambda(r, currentNamespace, failure: true)});",
            _ => $"{param}.Encode(ref encoder);",
        };

    private static string GetEncoderExtensionsClass(Entity entity) =>
        $"{entity.Name}SliceEncoderExtensions";

    private static string GetDecoderExtensionsClass(Entity entity) =>
        $"{entity.Name}SliceDecoderExtensions";

    private static int? GetFixedSize(IType type) => type switch
    {
        Builtin b => b.Kind switch
        {
            BuiltinKind.Bool or BuiltinKind.Int8 or BuiltinKind.UInt8 => 1,
            BuiltinKind.Int16 or BuiltinKind.UInt16 => 2,
            BuiltinKind.Int32 or BuiltinKind.UInt32 or BuiltinKind.Float32 => 4,
            BuiltinKind.Int64 or BuiltinKind.UInt64 or BuiltinKind.Float64 => 8,
            _ => null,
        },
        _ => null,
    };

    private string ResolveBaseType(IType type, string currentNamespace)
    {
        return type switch
        {
            Builtin builtin => builtin.CsType(),
            SequenceType seq =>
                $"global::System.Collections.Generic.IList<{TypeString(seq.ElementType.Type, seq.ElementTypeIsOptional, currentNamespace)}>",
            DictionaryType dict =>
                $"global::System.Collections.Generic.IDictionary<{TypeString(dict.KeyType.Type, false, currentNamespace)}, {TypeString(dict.ValueType.Type, dict.ValueTypeIsOptional, currentNamespace)}>",
            ResultType result =>
                $"Result<{TypeString(result.SuccessType.Type, result.SuccessTypeIsOptional, currentNamespace)}, {TypeString(result.FailureType.Type, result.FailureTypeIsOptional, currentNamespace)}>",
            _ => ResolveUserTypeName(type, currentNamespace),
        };
    }

    private static string ResolveUserTypeName(IType type, string currentNamespace)
    {
        Entity entity = type as Entity
            ?? throw new InvalidOperationException($"Type '{type.GetType().Name}' is not an Entity.");

        // Custom types use the cs::type attribute value directly as the C# type name.
        if (type is CustomType && entity.Attributes.FindAttribute(CsAttributes.CsType) is { } csTypeAttr)
        {
            return csTypeAttr.Args[0];
        }
        return ScopedIdentifier(entity.Name, entity.Namespace, currentNamespace);
    }

    private string DecodeExpression(IType type, string currentNamespace) =>
        type switch
        {
            Builtin builtin => $"decoder.Decode{builtin.Suffix()}()",
            SequenceType seq => DecodeSequence(seq, currentNamespace),
            DictionaryType dict => DecodeDictionary(dict, currentNamespace),
            EnumWithUnderlying e when !e.IsUnchecked =>
                $"{GetDecoderExtensionsClass(e)}.Decode{e.Name}(ref decoder)",
            EnumWithFields e =>
                $"{GetDecoderExtensionsClass(e)}.Decode{e.Name}(ref decoder)",
            CustomType c =>
                $"{GetDecoderExtensionsClass(c)}.Decode{c.Name}(ref decoder)",
            ResultType r =>
                $"decoder.DecodeResult({GetResultDecodeLambda(r, currentNamespace)}, {GetResultDecodeLambda(r, currentNamespace, failure: true)})",
            _ => $"new {ResolveUserTypeName(type, currentNamespace)}(ref decoder)",
        };

    private string EncodeSequence(SequenceType seq, string currentNamespace, string param)
    {
        IType elemType = seq.ElementType.Type;
        if (seq.ElementTypeIsOptional && (seq.ElementType.IsValueType() || elemType is CustomType))
        {
            string csOptType = TypeString(elemType, true, currentNamespace);
            string lambda = GetEncodeOptionalValueLambda(elemType, csOptType);
            return $"encoder.EncodeSequenceOfOptionals({param}, {lambda});";
        }
        string elementEncodeLambda = GetEncodeLambda(elemType, seq.ElementTypeIsOptional, currentNamespace);
        return $"encoder.EncodeSequence({param}, {elementEncodeLambda});";
    }

    private string DecodeSequence(SequenceType seq, string currentNamespace)
    {
        IType elemType = seq.ElementType.Type;
        if (seq.ElementTypeIsOptional && (seq.ElementType.IsValueType() || elemType is CustomType))
        {
            string baseType = ResolveBaseType(elemType, currentNamespace);
            string decodeExpr = DecodeExpression(elemType, currentNamespace);
            return $"decoder.DecodeSequenceOfOptionals((ref SliceDecoder decoder) => ({baseType}?){decodeExpr})";
        }
        string elementDecodeLambda = GetDecodeLambda(elemType, seq.ElementTypeIsOptional, currentNamespace);
        return $"decoder.DecodeSequence({elementDecodeLambda})";
    }

    private static string GetEncodeOptionalValueLambda(IType elemType, string csOptType)
    {
        if (elemType is Entity entity and (EnumWithUnderlying or EnumWithFields or CustomType))
        {
            string extClass = GetEncoderExtensionsClass(entity);
            string name = entity.Name;
            return $"(ref SliceEncoder encoder, {csOptType} value) => {extClass}.Encode{name}(ref encoder, (value ?? default!))";
        }
        return $"(ref SliceEncoder encoder, {csOptType} value) => (value ?? default!).Encode(ref encoder)";
    }

    // Returns a decode lambda for a result success/failure type, handling optional inner types with an inline bool
    // marker (decoder.DecodeBool()) rather than the bit-sequence pattern used for struct fields.
    private string GetResultDecodeLambda(ResultType result, string currentNamespace, bool failure = false)
    {
        IType type = failure ? result.FailureType.Type : result.SuccessType.Type;
        bool isOptional = failure ? result.FailureTypeIsOptional : result.SuccessTypeIsOptional;

        if (!isOptional)
        {
            return GetDecodeLambda(type, false, currentNamespace);
        }
        string csType = TypeString(type, true, currentNamespace);
        string decodeExpr = DecodeExpression(type, currentNamespace);
        return $"(ref SliceDecoder decoder) => decoder.DecodeBool() ? ({csType}){decodeExpr} : null";
    }

    // Returns an encode lambda for a result success/failure type, handling optional inner types with an inline bool
    // marker (encoder.EncodeBool()) rather than the bit-sequence pattern used for struct fields.
    private string GetResultEncodeLambda(ResultType result, string currentNamespace, bool failure = false)
    {
        IType type = failure ? result.FailureType.Type : result.SuccessType.Type;
        bool isOptional = failure ? result.FailureTypeIsOptional : result.SuccessTypeIsOptional;
        TypeRef typeRef = failure ? result.FailureType : result.SuccessType;

        if (!isOptional)
        {
            return GetEncodeLambda(type, false, currentNamespace);
        }
        string csType = TypeString(type, true, currentNamespace);
        string valueParam = typeRef.IsValueType() ? "value!.Value" : "value!";
        string encodeBody = EncodeExpression(type, currentNamespace, valueParam);
        return $$"""
            (ref SliceEncoder encoder, {{csType}} value) =>
            {
                encoder.EncodeBool(value is not null);
                if (value is not null)
                {
                    {{encodeBody}}
                }
            }
            """;
    }

    private string EncodeDictionary(DictionaryType dict, string currentNamespace, string param)
    {
        string keyEncodeLambda = GetEncodeLambda(dict.KeyType.Type, false, currentNamespace);
        string valueEncodeLambda = GetEncodeLambda(dict.ValueType.Type, dict.ValueTypeIsOptional, currentNamespace);
        return $"encoder.EncodeDictionary({param}, {keyEncodeLambda}, {valueEncodeLambda});";
    }

    private string DecodeDictionary(DictionaryType dict, string currentNamespace)
    {
        string keyDecodeLambda = GetDecodeLambda(dict.KeyType.Type, false, currentNamespace);
        string valueDecodeLambda = GetDecodeLambda(dict.ValueType.Type, dict.ValueTypeIsOptional, currentNamespace);
        return $"decoder.DecodeDictionary({keyDecodeLambda}, {valueDecodeLambda})";
    }

    private string GetEncodeLambda(IType type, bool isOptional, string currentNamespace)
    {
        if (type is Builtin b)
        {
            string csType = b.CsType();
            return $"(ref SliceEncoder encoder, {csType} value) => encoder.Encode{b.Suffix()}(value)";
        }
        else if (type is Entity entity and (EnumWithUnderlying or EnumWithFields or CustomType))
        {
            string csType = TypeString(type, isOptional, currentNamespace);
            string extensionClass = GetEncoderExtensionsClass(entity);
            string name = entity.Name;
            return $"(ref SliceEncoder encoder, {csType} value) => {extensionClass}.Encode{name}(ref encoder, value)";
        }
        else if (type is ResultType r)
        {
            string csType = TypeString(type, isOptional, currentNamespace);
            return $"(ref SliceEncoder encoder, {csType} value) => encoder.EncodeResult(value, {GetResultEncodeLambda(r, currentNamespace)}, {GetResultEncodeLambda(r, currentNamespace, failure: true)})";
        }
        else
        {
            string csType = TypeString(type, isOptional, currentNamespace);
            return $"(ref SliceEncoder encoder, {csType} value) => value.Encode(ref encoder)";
        }
    }

    private string GetDecodeLambda(IType type, bool isOptional, string currentNamespace)
    {
        if (type is Builtin b)
        {
            return $"(ref SliceDecoder decoder) => decoder.Decode{b.Suffix()}()";
        }
        else if (type is Entity entity and (EnumWithUnderlying or EnumWithFields or CustomType))
        {
            string extensionClass = GetDecoderExtensionsClass(entity);
            string name = entity.Name;
            return $"(ref SliceDecoder decoder) => {extensionClass}.Decode{name}(ref decoder)";
        }
        else if (type is ResultType r)
        {
            return $"(ref SliceDecoder decoder) => decoder.DecodeResult({GetResultDecodeLambda(r, currentNamespace)}, {GetResultDecodeLambda(r, currentNamespace, failure: true)})";
        }
        else
        {
            string csType = TypeString(type, isOptional, currentNamespace);
            return $"(ref SliceDecoder decoder) => new {csType}(ref decoder)";
        }
    }
}
