// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for <see cref="TypeRef"/>.</summary>
internal static class TypeRefExtensions
{
    /// <summary>Generates decode expression for a type reference. When the TypeRef has a cs::type attribute,
    /// it is passed through as the concrete type for dictionary/sequence factory construction.</summary>
    internal static string DecodeExpression(this TypeRef typeRef, string currentNamespace)
    {
        if (typeRef.Type is DictionaryType or SequenceType
            && typeRef.Attributes.FindAttribute(CSAttributes.CSType) is Symbols.Attribute csTypeAttr)
        {
            return typeRef.Type.DecodeExpression(currentNamespace, concreteType: csTypeAttr.Args[0]);
        }
        return typeRef.Type.DecodeExpression(currentNamespace);
    }

    /// <summary>Generates encode expression for a type reference.</summary>
    internal static string EncodeExpression(
        this TypeRef typeRef,
        string currentNamespace,
        string param,
        string encoderName = "encoder") =>
        typeRef.Type.EncodeExpression(currentNamespace, param, encoderName);

    /// <summary>Returns the C# type string for a field type reference.</summary>
    internal static string FieldTypeString(this TypeRef typeRef, bool isOptional, string currentNamespace)
    {
        string baseType = typeRef.Type.ToTypeString(currentNamespace);
        return isOptional ? $"{baseType}?" : baseType;
    }

    /// <summary>Returns an encode lambda for a type reference. For optional types, the lambda parameter is nullable
    /// and the value expression uses the appropriate null-forgiving pattern based on the type.</summary>
    internal static string GetEncodeLambda(this TypeRef typeRef, bool isOptional, string currentNamespace)
    {
        if (!isOptional)
        {
            return typeRef.Type.GetEncodeLambda(false, currentNamespace);
        }

        string csType = typeRef.Type.ToTypeString(currentNamespace) + "?";
        string encodeExpr = typeRef.Type.EncodeExpression(currentNamespace, typeRef.UnwrapNonNullOptional("value"));
        return $"(ref SliceEncoder encoder, {csType} value) => {encodeExpr}";
    }

    /// <summary>Returns an encode lambda for an optional type reference that writes a bool null marker before the
    /// value, for use where the caller has no bit sequence.</summary>
    internal static string GetEncodeLambdaWithNullMarker(this TypeRef typeRef, string currentNamespace)
    {
        string csType = typeRef.FieldTypeString(true, currentNamespace);
        CodeBlock encodeBody = typeRef.EncodeExpression(currentNamespace, typeRef.UnwrapNonNullOptional("value"));
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

    /// <summary>Returns the C# type string for an incoming parameter (decode target). Sequences map to arrays,
    /// dictionaries map to <c>Dictionary&lt;K,V&gt;</c>. Respects cs::type attribute.</summary>
    internal static string IncomingParameterTypeString(this TypeRef typeRef, bool isOptional, string currentNamespace)
    {
        string baseType = typeRef.Type switch
        {
            SequenceType _ when typeRef.Attributes.FindAttribute(CSAttributes.CSType) is Symbols.Attribute attr =>
                attr.Args[0],
            SequenceType seq =>
                $"{seq.ElementType.FieldTypeString(seq.ElementTypeIsOptional, currentNamespace)}[]",
            DictionaryType _ when typeRef.Attributes.FindAttribute(CSAttributes.CSType) is Symbols.Attribute attr =>
                attr.Args[0],
            DictionaryType dict =>
                $"global::System.Collections.Generic.Dictionary<{dict.KeyType.FieldTypeString(false, currentNamespace)}, {dict.ValueType.FieldTypeString(dict.ValueTypeIsOptional, currentNamespace)}>",
            _ => typeRef.Type.ToTypeString(currentNamespace),
        };
        return isOptional ? $"{baseType}?" : baseType;
    }

    /// <summary>Returns the C# type string for an outgoing parameter (encode source). Sequences of fixed-size
    /// primitives map to <c>ReadOnlyMemory&lt;T&gt;</c>, other sequences to <c>IEnumerable&lt;T&gt;</c>, and
    /// dictionaries to <c>IEnumerable&lt;KeyValuePair&lt;K,V&gt;&gt;</c>.</summary>
    internal static string OutgoingParameterTypeString(this TypeRef typeRef, bool isOptional, string currentNamespace)
    {
        bool ignoreOptional = false;
        string baseType;

        if (typeRef.Type is SequenceType seq
            && !typeRef.Attributes.HasAttribute(CSAttributes.CSType)
            && !seq.ElementTypeIsOptional
            && seq.ElementType.FixedSize is not null)
        {
            // Fixed-size primitive sequences use ReadOnlyMemory; the mapping is the same for
            // optional and non-optional types.
            ignoreOptional = true;
            string elemType = seq.ElementType.FieldTypeString(seq.ElementTypeIsOptional, currentNamespace);
            baseType = $"global::System.ReadOnlyMemory<{elemType}>";
        }
        else if (typeRef.Type is SequenceType seq2)
        {
            string elemType = seq2.ElementType.FieldTypeString(seq2.ElementTypeIsOptional, currentNamespace);
            baseType = $"global::System.Collections.Generic.IEnumerable<{elemType}>";
        }
        else if (typeRef.Type is DictionaryType dict)
        {
            string keyType = dict.KeyType.FieldTypeString(false, currentNamespace);
            string valueType = dict.ValueType.FieldTypeString(dict.ValueTypeIsOptional, currentNamespace);
            baseType = $"global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<{keyType}, {valueType}>>";
        }
        else
        {
            baseType = typeRef.Type.ToTypeString(currentNamespace);
        }

        return (isOptional && !ignoreOptional) ? $"{baseType}?" : baseType;
    }

    /// <summary>Returns the expression that unwraps a non-null optional value for encoding. A custom type can map to
    /// either a C# value type or a reference type, so it uses <c>??</c> instead of <c>!</c>.</summary>
    internal static string UnwrapNonNullOptional(this TypeRef typeRef, string param) =>
        typeRef.Type is CustomType ? $"({param} ?? default!)" : typeRef.IsValueType ? $"{param}!.Value" : $"{param}!";

    extension(TypeRef value)
    {
        /// <summary>Returns the fixed wire size for a type reference, or null if variable-size.</summary>
        internal int? FixedSize => value.Type switch
        {
            Builtin b => BuiltinFixedSize(b.Kind),
            BasicEnum e => BuiltinFixedSize(e.Underlying.Kind),
            _ => null,
        };

        private static int? BuiltinFixedSize(BuiltinKind kind) => kind switch
        {
            BuiltinKind.Bool or BuiltinKind.Int8 or BuiltinKind.UInt8 => 1,
            BuiltinKind.Int16 or BuiltinKind.UInt16 => 2,
            BuiltinKind.Int32 or BuiltinKind.UInt32 or BuiltinKind.Float32 => 4,
            BuiltinKind.Int64 or BuiltinKind.UInt64 or BuiltinKind.Float64 => 8,
            _ => null,
        };

        /// <summary>Gets a value indicating whether the referenced type is a C# value type.</summary>
        internal bool IsValueType => value.Type switch
        {
            Builtin b => b.Kind != BuiltinKind.String,
            Struct => true,
            BasicEnum => true,
            _ => false,
        };
    }
}
