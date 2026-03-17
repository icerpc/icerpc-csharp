// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for <see cref="TypeRef"/>.</summary>
internal static class TypeRefExtensions
{

    /// <summary>Generates decode expression for a type reference.</summary>
    internal static string DecodeExpression(this TypeRef typeRef, string currentNamespace) =>
        typeRef.Type.DecodeExpression(currentNamespace);


    /// <summary>Generates encode expression for a type reference.</summary>
    internal static string EncodeExpression(this TypeRef typeRef, string currentNamespace, string param) =>
        typeRef.Type.EncodeExpression(currentNamespace, param);


    /// <summary>Returns the C# type string for a field type reference.</summary>
    internal static string FieldTypeString(this TypeRef typeRef, bool isOptional, string currentNamespace)
    {
        string baseType = typeRef.Type.ToTypeString(currentNamespace);
        return isOptional ? $"{baseType}?" : baseType;
    }

    /// <summary>Returns an encode lambda for a type reference.</summary>
    internal static string GetEncodeLambda(this TypeRef typeRef, bool isOptional, string currentNamespace) =>
        typeRef.Type.GetEncodeLambda(isOptional, currentNamespace);

    extension(TypeRef value)
    {
        /// <summary>Returns the fixed wire size for a type reference, or null if variable-size.</summary>
        internal int? FixedSize => value.Type switch
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

        /// <summary>Gets a value indicating whether the referenced type is a C# value type.</summary>
        internal bool IsValueType => value.Type switch
        {
            Builtin b => b.Kind != BuiltinKind.String,
            Struct => true,
            EnumWithUnderlying => true,
            _ => false,
        };
    }
}
