// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;
using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Abstract base class for code generators. Owns the type registry (symbol-to-namespace mapping) and
/// provides type resolution and field helper methods.</summary>
internal class Generator
{
    /// <summary>Gets the access modifier for an entity ("public" or "internal").</summary>
    protected static string AccessModifier(EntityInfo entity) =>
        entity.Attributes.HasAttribute(Attribute.CsInternal) ? "internal" : "public";

    /// <summary>Qualifies a type name relative to the current namespace.</summary>
    protected static string ScopedIdentifier(string identifier, string identifierNamespace, string currentNamespace) =>
        currentNamespace == identifierNamespace
            ? identifier
            : $"global::{identifierNamespace}.{identifier}";

    /// <summary>Gets the EntityInfo from a named symbol, or null for anonymous/builtin symbols.</summary>
    protected static EntityInfo? GetEntityInfo(Symbol symbol) => symbol switch
    {
        Struct s => s.EntityInfo,
        EnumWithUnderlying e => e.EntityInfo,
        EnumWithFields e => e.EntityInfo,
        Interface i => i.EntityInfo,
        CustomType c => c.EntityInfo,
        TypeAlias t => t.EntityInfo,
        _ => null,
    };

    /// <summary>Returns fields sorted: non-tagged in original order, then tagged sorted by tag value.</summary>
    protected static IReadOnlyList<Field> GetSortedFields(ImmutableList<Field> fields)
    {
        var nonTagged = fields.Where(f => !f.IsTagged).ToList();
        var tagged = fields.Where(f => f.IsTagged).OrderBy(f => f.Tag!.Value).ToList();
        nonTagged.AddRange(tagged);
        return nonTagged;
    }

    /// <summary>Counts non-tagged optional fields (for Slice2 bit sequence sizing).</summary>
    protected static int GetBitSequenceSize(ImmutableList<Field> fields) =>
        fields.Count(f => !f.IsTagged && f.Type.IsOptional);

    /// <summary>Resolves a TypeRef to its C# type string for field declarations.</summary>
    protected string FieldTypeString(TypeRef typeRef, string currentNamespace)
    {
        string baseType = ResolveBaseType(typeRef.Symbol, currentNamespace);
        return typeRef.IsOptional ? $"{baseType}?" : baseType;
    }

    /// <summary>Generates encode code for a non-tagged field.</summary>
    protected string EncodeField(Field field, string currentNamespace) =>
        EncodeExpression(field.Type, currentNamespace, $"this.{field.EntityInfo.Name}");

    /// <summary>Generates decode expression for a non-tagged field.</summary>
    protected string DecodeField(Field field, string currentNamespace) =>
        DecodeExpression(field.Type, currentNamespace);

    /// <summary>Generates encode code for a tagged field.</summary>
    protected string EncodeTaggedField(Field field, string currentNamespace)
    {
        string param = $"this.{field.EntityInfo.Name}";
        int tag = field.Tag!.Value;

        bool isValueType = field.Type.IsValueType;
        string csType = ResolveBaseType(field.Type.Symbol, currentNamespace);
        string varName = $"{field.EntityInfo.ParameterName}_";
        string encodeLambda = GetEncodeLambda(field.Type, currentNamespace);

        if (isValueType)
        {
            int? fixedSize = GetFixedSize(field.Type);
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
            string decodeExpr = DecodeExpression(field.Type, currentNamespace);
            string csType = ResolveBaseType(field.Type.Symbol, currentNamespace);
            return $"decoder.DecodeTagged({tag}, (ref SliceDecoder decoder) => ({csType}?){decodeExpr})";
        }
        else if (field.Type.IsOptional)
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
        code.WriteCsAttributes(field.EntityInfo.Attributes);

        string typeString = FieldTypeString(field.Type, currentNamespace);
        string fieldName = field.EntityInfo.Name;
        string required = field.IsRequired ? "required " : "";
        bool fieldReadonly = field.EntityInfo.Attributes.HasAttribute(Attribute.CsReadonly);
        string accessor = (parentReadonly || fieldReadonly) ? "{ get; init; }" : "{ get; set; }";

        code.WriteLine($"{accessModifier} {required}{typeString} {fieldName} {accessor}");

        return code;
    }

    protected string EncodeExpression(TypeRef typeRef, string currentNamespace, string param) =>
        typeRef.Symbol switch
        {
            Builtin builtin => $"encoder.Encode{builtin.Suffix}({param});",
            SequenceType seq => EncodeSequence(seq, currentNamespace, param),
            DictionaryType dict => EncodeDictionary(dict, currentNamespace, param),
            EnumWithUnderlying e when !e.IsUnchecked =>
                $"{GetEncoderExtensionsClass(e.EntityInfo)}.Encode{e.EntityInfo.Name}(ref encoder, {param});",
            EnumWithFields e =>
                $"{GetEncoderExtensionsClass(e.EntityInfo)}.Encode{e.EntityInfo.Name}(ref encoder, {param});",
            _ => $"{param}.Encode(ref encoder);",
        };

    private static string AsNamespace(Module module)
    {
        if (module.Attributes.FindAttribute(Attribute.CsNamespace) is { } attr)
        {
            return attr.Args[0];
        }

        // Convert "Foo::Bar::Baz" to "Foo.Bar.Baz" with PascalCase on each segment.
        string[] segments = module.Identifier.Split("::");
        return string.Join(".", segments.Select(s => s.ToPascalCase()));
    }

    private static string GetEncoderExtensionsClass(EntityInfo entityInfo) =>
        $"{entityInfo.Name}SliceEncoderExtensions";

    private static string GetDecoderExtensionsClass(EntityInfo entityInfo) =>
        $"{entityInfo.Name}SliceDecoderExtensions";

    private static int? GetFixedSize(TypeRef typeRef) => typeRef.Symbol switch
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

    private string ResolveBaseType(Symbol symbol, string currentNamespace)
    {
        return symbol switch
        {
            Builtin builtin => builtin.CsType,
            SequenceType seq =>
                $"global::System.Collections.Generic.IList<{FieldTypeString(seq.ElementType, currentNamespace)}>",
            DictionaryType dict =>
                $"global::System.Collections.Generic.IDictionary<{FieldTypeString(dict.KeyType, currentNamespace)}, {FieldTypeString(dict.ValueType, currentNamespace)}>",
            ResultType result =>
                $"Result<{FieldTypeString(result.SuccessType, currentNamespace)}, {FieldTypeString(result.FailureType, currentNamespace)}>",
            _ => ResolveUserTypeName(symbol, currentNamespace),
        };
    }

    private static string ResolveUserTypeName(Symbol symbol, string currentNamespace)
    {
        EntityInfo entityInfo = GetEntityInfo(symbol)
            ?? throw new InvalidOperationException($"Symbol '{symbol.GetType().Name}' does not have an EntityInfo.");

        string typeName = entityInfo.Name;
        string typeNamespace = entityInfo.Namespace;

        if (typeNamespace != currentNamespace)
        {
            return ScopedIdentifier(typeName, typeNamespace, currentNamespace);
        }

        return typeName;
    }

    private string DecodeExpression(TypeRef typeRef, string currentNamespace) =>
        typeRef.Symbol switch
        {
            Builtin builtin => $"decoder.Decode{builtin.Suffix}()",
            SequenceType seq => DecodeSequence(seq, currentNamespace),
            DictionaryType dict => DecodeDictionary(dict, currentNamespace),
            EnumWithUnderlying e when !e.IsUnchecked =>
                $"{GetDecoderExtensionsClass(e.EntityInfo)}.Decode{e.EntityInfo.Name}(ref decoder)",
            EnumWithFields e =>
                $"{GetDecoderExtensionsClass(e.EntityInfo)}.Decode{e.EntityInfo.Name}(ref decoder)",
            _ => $"new {ResolveUserTypeName(typeRef.Symbol, currentNamespace)}(ref decoder)",
        };

    private string EncodeSequence(SequenceType seq, string currentNamespace, string param)
    {
        string elementEncodeLambda = GetEncodeLambda(seq.ElementType, currentNamespace);
        return $"encoder.EncodeSequence({param}, {elementEncodeLambda});";
    }

    private string DecodeSequence(SequenceType seq, string currentNamespace)
    {
        string elementDecodeLambda = GetDecodeLambda(seq.ElementType, currentNamespace);
        return $"decoder.DecodeSequence({elementDecodeLambda})";
    }

    private string EncodeDictionary(DictionaryType dict, string currentNamespace, string param)
    {
        string keyEncodeLambda = GetEncodeLambda(dict.KeyType, currentNamespace);
        string valueEncodeLambda = GetEncodeLambda(dict.ValueType, currentNamespace);
        return $"encoder.EncodeDictionary({param}, {keyEncodeLambda}, {valueEncodeLambda});";
    }

    private string DecodeDictionary(DictionaryType dict, string currentNamespace)
    {
        string keyDecodeLambda = GetDecodeLambda(dict.KeyType, currentNamespace);
        string valueDecodeLambda = GetDecodeLambda(dict.ValueType, currentNamespace);
        return $"decoder.DecodeDictionary({keyDecodeLambda}, {valueDecodeLambda})";
    }

    private string GetEncodeLambda(TypeRef typeRef, string currentNamespace)
    {
        if (typeRef.Symbol is Builtin b)
        {
            string csType = b.CsType;
            return $"(ref SliceEncoder encoder, {csType} value) => encoder.Encode{b.Suffix}(value)";
        }
        else if (typeRef.Symbol is EnumWithUnderlying or EnumWithFields)
        {
            EntityInfo entityInfo = GetEntityInfo(typeRef.Symbol)!;
            string csType = FieldTypeString(typeRef, currentNamespace);
            string extensionClass = GetEncoderExtensionsClass(entityInfo);
            string name = entityInfo.Name;
            return $"(ref SliceEncoder encoder, {csType} value) => {extensionClass}.Encode{name}(ref encoder, value)";
        }
        else
        {
            string csType = FieldTypeString(typeRef, currentNamespace);
            return $"(ref SliceEncoder encoder, {csType} value) => value.Encode(ref encoder)";
        }
    }

    private string GetDecodeLambda(TypeRef typeRef, string currentNamespace)
    {
        if (typeRef.Symbol is Builtin b)
        {
            return $"(ref SliceDecoder decoder) => decoder.Decode{b.Suffix}()";
        }
        else if (typeRef.Symbol is EnumWithUnderlying or EnumWithFields)
        {
            EntityInfo entityInfo = GetEntityInfo(typeRef.Symbol)!;
            string extensionClass = GetDecoderExtensionsClass(entityInfo);
            string name = entityInfo.Name;
            return $"(ref SliceDecoder decoder) => {extensionClass}.Decode{name}(ref decoder)";
        }
        else
        {
            string csType = FieldTypeString(typeRef, currentNamespace);
            return $"(ref SliceDecoder decoder) => new {csType}(ref decoder)";
        }
    }
}
