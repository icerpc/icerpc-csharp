// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for <see cref="TypeRef"/>.</summary>
internal static class TypeRefExtensions
{
    /// <summary>Gets a value indicating whether the referenced type is a C# value type.</summary>
    internal static bool IsValueType(this TypeRef typeRef) => typeRef.Type switch
    {
        Builtin b => b.Kind != BuiltinKind.String,
        Struct => true,
        EnumWithUnderlying => true,
        _ => false,
    };
}
