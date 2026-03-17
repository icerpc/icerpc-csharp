// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Converts decoded Slice compiler types into rich symbol types intended to use in code generators.</summary>
public sealed class SymbolConverter
{
    /// <summary>Converts source files into rich symbol types with all TypeRefs resolved. Reference files are used for
    /// type resolution but are not included in the output.</summary>
    public static ImmutableList<SliceFile> ConvertFiles(
        IEnumerable<Compiler.SliceFile> sourceFiles,
        IEnumerable<Compiler.SliceFile> referenceFiles)
    {
        var converter = new SymbolConverter(sourceFiles.Concat(referenceFiles));
        return sourceFiles.Select(converter.ConvertFile).ToImmutableList();
    }

    private static readonly Dictionary<string, Builtin> _builtins = new(StringComparer.Ordinal)
    {
        ["bool"] = new() { Kind = BuiltinKind.Bool },
        ["int8"] = new() { Kind = BuiltinKind.Int8 },
        ["uint8"] = new() { Kind = BuiltinKind.UInt8 },
        ["int16"] = new() { Kind = BuiltinKind.Int16 },
        ["uint16"] = new() { Kind = BuiltinKind.UInt16 },
        ["int32"] = new() { Kind = BuiltinKind.Int32 },
        ["uint32"] = new() { Kind = BuiltinKind.UInt32 },
        ["varint32"] = new() { Kind = BuiltinKind.VarInt32 },
        ["varuint32"] = new() { Kind = BuiltinKind.VarUInt32 },
        ["int64"] = new() { Kind = BuiltinKind.Int64 },
        ["uint64"] = new() { Kind = BuiltinKind.UInt64 },
        ["varint62"] = new() { Kind = BuiltinKind.VarInt62 },
        ["varuint62"] = new() { Kind = BuiltinKind.VarUInt62 },
        ["float32"] = new() { Kind = BuiltinKind.Float32 },
        ["float64"] = new() { Kind = BuiltinKind.Float64 },
        ["string"] = new() { Kind = BuiltinKind.String },
    };

    // Index of all named types across all files, keyed by fully-scoped TypeId.
    private readonly Dictionary<string, (Compiler.SliceFile File, Compiler.Symbol Symbol)> _named;

    // Cache of converted named symbols, keyed by fully-scoped TypeId.
    private readonly Dictionary<string, ISymbol> _cache = new(StringComparer.Ordinal);

    private SymbolConverter(IEnumerable<Compiler.SliceFile> allFiles)
    {
        _named = new(StringComparer.Ordinal);

        foreach (Compiler.SliceFile file in allFiles)
        {
            string moduleScope = file.ModuleDeclaration.Identifier;

            foreach (Compiler.Symbol symbol in file.Contents)
            {
                if (GetNamedIdentifier(symbol) is string id)
                {
                    string key = string.IsNullOrEmpty(moduleScope) ? id : $"{moduleScope}::{id}";
                    _named.TryAdd(key, (file, symbol));
                }
            }
        }
    }

    private SliceFile ConvertFile(Compiler.SliceFile file)
    {
        string moduleScope = file.ModuleDeclaration.Identifier;
        Module module = ConvertModule(file.ModuleDeclaration);

        var contents = ImmutableList.CreateBuilder<ISymbol>();
        for (int i = 0; i < file.Contents.Count; i++)
        {
            Compiler.Symbol raw = file.Contents[i];
            string? id = GetNamedIdentifier(raw);

            if (id is not null)
            {
                // Named type — resolve through cache using its scoped TypeId.
                string key = string.IsNullOrEmpty(moduleScope) ? id : $"{moduleScope}::{id}";
                contents.Add(ResolveNamedSymbol(key));
            }
            else
            {
                // Anonymous type — convert inline (scoped to this file).
                contents.Add(ConvertSymbol(raw, file, module));
            }
        }

        return new()
        {
            Path = file.Path,
            Module = module,
            Attributes = ConvertAttributes(file.Attributes),
            Contents = contents.ToImmutable(),
        };
    }

    /// <summary>Resolves a TypeId to an IType, handling primitives, anonymous types, and named types.</summary>
    private IType ResolveTypeId(string typeId, Compiler.SliceFile currentFile)
    {
        // Builtin.
        if (_builtins.TryGetValue(typeId, out Builtin? prim))
        {
            return prim;
        }

        // Anonymous type (numeric index into currentFile.Contents).
        if (int.TryParse(typeId, out int index))
        {
            Module module = ConvertModule(currentFile.ModuleDeclaration);
            return (IType)ConvertSymbol(currentFile.Contents[index], currentFile, module);
        }

        // Named type.
        return (IType)ResolveNamedSymbol(typeId);
    }

    private ISymbol ResolveNamedSymbol(string typeId)
    {
        Console.WriteLine($"Resolving TypeId: {typeId}");
        if (_cache.TryGetValue(typeId, out ISymbol? cached))
        {
            return cached;
        }

        (Compiler.SliceFile file, Compiler.Symbol symbol) = _named[typeId];
        Module module = ConvertModule(file.ModuleDeclaration);
        ISymbol converted = ConvertSymbol(symbol, file, module);
        _cache[typeId] = converted;
        return converted;
    }

    private ISymbol ConvertSymbol(Compiler.Symbol symbol, Compiler.SliceFile file, Module module) =>
        symbol switch
        {
            Compiler.Symbol.Struct s => ConvertStruct(s.V, file, module),
            Compiler.Symbol.Enum e => ConvertEnum(e.V, file, module),
            Compiler.Symbol.Interface i => ConvertInterface(i.V, file, module),
            Compiler.Symbol.CustomType c => new CustomType
            {
                Identifier = c.V.EntityInfo.Identifier,
                Attributes = ConvertAttributes(c.V.EntityInfo.Attributes),
                Module = module,
            },
            Compiler.Symbol.TypeAlias t => new TypeAlias
            {
                Identifier = t.V.EntityInfo.Identifier,
                Attributes = ConvertAttributes(t.V.EntityInfo.Attributes),
                Module = module,
                UnderlyingType = ConvertTypeRef(t.V.UnderlyingType, file),
            },
            Compiler.Symbol.SequenceType s => new SequenceType
            {
                ElementType = ConvertTypeRef(s.V.ElementType, file),
                ElementTypeIsOptional = s.V.ElementType.IsOptional,
            },
            Compiler.Symbol.DictionaryType d => new DictionaryType
            {
                KeyType = ConvertTypeRef(d.V.KeyType, file),
                ValueType = ConvertTypeRef(d.V.ValueType, file),
                ValueTypeIsOptional = d.V.ValueType.IsOptional,
            },
            Compiler.Symbol.ResultType r => new ResultType
            {
                SuccessType = ConvertTypeRef(r.V.SuccessType, file),
                FailureType = ConvertTypeRef(r.V.FailureType, file),
                SuccessTypeIsOptional = r.V.SuccessType.IsOptional,
                FailureTypeIsOptional = r.V.FailureType.IsOptional,
            },
            _ => throw new InvalidOperationException($"Unknown symbol type: {symbol.GetType().Name}"),
        };

    private Struct ConvertStruct(Compiler.Struct raw, Compiler.SliceFile file, Module module) => new()
    {
        Identifier = raw.EntityInfo.Identifier,
        Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
        Module = module,
        IsCompact = raw.IsCompact,
        Fields = raw.Fields.Select(f => ConvertField(f, file, module)).ToImmutableList(),
    };

    private ISymbol ConvertEnum(Compiler.Enum raw, Compiler.SliceFile file, Module module)
    {
        if (raw.Underlying is string u && _builtins.TryGetValue(u, out var builtin))
        {
            return builtin.Kind switch
            {
                BuiltinKind.Int8 => CreateEnumWithUnderlying(
                    raw,
                    module,
                    builtin,
                    (abs, isNegative) => isNegative ? (sbyte)-(long)abs : (sbyte)abs),
                BuiltinKind.UInt8 => CreateEnumWithUnderlying(
                    raw,
                    module,
                    builtin,
                    (abs, _) => (byte)abs),
                BuiltinKind.Int16 => CreateEnumWithUnderlying(
                    raw,
                    module,
                    builtin,
                    (abs, isNegative) => isNegative ? (short)-(long)abs : (short)abs),
                BuiltinKind.UInt16 => CreateEnumWithUnderlying(
                    raw,
                    module,
                    builtin,
                    (abs, _) => (ushort)abs),
                BuiltinKind.Int32 or BuiltinKind.VarInt32 => CreateEnumWithUnderlying(
                    raw,
                    module,
                    builtin,
                    (abs, isNegative) => isNegative ? (int)-(long)abs : (int)abs),
                BuiltinKind.UInt32 or BuiltinKind.VarUInt32 => CreateEnumWithUnderlying(
                    raw,
                    module,
                    builtin,
                    (abs, _) => (uint)abs),
                BuiltinKind.Int64 or BuiltinKind.VarInt62 => CreateEnumWithUnderlying(
                    raw,
                    module,
                    builtin,
                    (abs, isNegative) => isNegative ? -(long)abs : (long)abs),
                BuiltinKind.UInt64 or BuiltinKind.VarUInt62 => CreateEnumWithUnderlying(
                    raw,
                    module,
                    builtin,
                    (abs, _) => abs),
                _ => throw new InvalidOperationException(
                    $"Unsupported enum underlying type: {builtin.Kind}"),
            };
        }
        else
        {
            return new EnumWithFields
            {
                Identifier = raw.EntityInfo.Identifier,
                Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
                Module = module,
                IsCompact = raw.IsCompact,
                IsUnchecked = raw.IsUnchecked,
                Enumerators = raw.Enumerators.Select(e => new EnumWithFields.Enumerator
                {
                    Identifier = e.EntityInfo.Identifier,
                    Attributes = ConvertAttributes(e.EntityInfo.Attributes),
                    Module = module,
                    Discriminant = (int)e.Value.AbsoluteValue,
                    Fields = e.Fields.Select(f => ConvertField(f, file, module)).ToImmutableList(),
                }).ToImmutableList(),
            };
        }
    }

    private Interface ConvertInterface(Compiler.Interface raw, Compiler.SliceFile file, Module module) =>
        new()
        {
            Identifier = raw.EntityInfo.Identifier,
            Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
            Module = module,
            Bases = raw.Bases
            .Select(baseId => ResolveNamedSymbol(baseId))
            .OfType<Interface>()
            .ToImmutableList(),
            Operations = raw.Operations.Select(op => ConvertOperation(op, file, module)).ToImmutableList(),
        };

    private Operation ConvertOperation(
        Compiler.Operation raw,
        Compiler.SliceFile file,
        Module module) => new()
        {
            Identifier = raw.EntityInfo.Identifier,
            Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
            Module = module,
            IsIdempotent = raw.IsIdempotent,
            Parameters = raw.Parameters.Select(f => ConvertField(f, file, module)).ToImmutableList(),
            HasStreamedParameter = raw.HasStreamedParameter,
            ReturnType = raw.ReturnType.Select(f => ConvertField(f, file, module)).ToImmutableList(),
            HasStreamedReturn = raw.HasStreamedReturn,
        };

    private static EnumWithUnderlying<T> CreateEnumWithUnderlying<T>(
        Compiler.Enum raw,
        Module module,
        Builtin builtin,
        Func<ulong, bool, T> toValue) where T : struct => new()
        {
            Identifier = raw.EntityInfo.Identifier,
            Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
            Module = module,
            IsUnchecked = raw.IsUnchecked,
            Underlying = builtin,
            Enumerators = raw.Enumerators.Select(e => new EnumWithUnderlying<T>.Enumerator
            {
                Identifier = e.EntityInfo.Identifier,
                Attributes = ConvertAttributes(e.EntityInfo.Attributes),
                Module = module,
                Value = toValue(e.Value.AbsoluteValue, e.Value.IsNegative),
            }).ToImmutableList(),
        };

    private Field ConvertField(Compiler.Field raw, Compiler.SliceFile file, Module module) => new()
    {
        Identifier = raw.EntityInfo.Identifier,
        Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
        Module = module,
        Tag = raw.Tag,
        DataType = ConvertTypeRef(raw.DataType, file),
        DataTypeIsOptional = raw.DataType.IsOptional,
    };

    private TypeRef ConvertTypeRef(Compiler.TypeRef raw, Compiler.SliceFile file) => new()
    {
        Type = ResolveTypeId(raw.TypeId, file),
        Attributes = ConvertAttributes(raw.TypeAttributes),
    };

    private static Module ConvertModule(Compiler.Module raw) => new()
    {
        Identifier = raw.Identifier,
        Attributes = ConvertAttributes(raw.Attributes),
    };

    private static ImmutableList<Attribute> ConvertAttributes(IList<Compiler.Attribute> raw) =>
        raw.Select(a => new Attribute
        {
            Directive = a.Directive,
            Args = [.. a.Args],
        }).ToImmutableList();

    private static string? GetNamedIdentifier(Compiler.Symbol symbol) => symbol switch
    {
        Compiler.Symbol.Struct s => s.V.EntityInfo.Identifier,
        Compiler.Symbol.Enum e => e.V.EntityInfo.Identifier,
        Compiler.Symbol.Interface i => i.V.EntityInfo.Identifier,
        Compiler.Symbol.CustomType c => c.V.EntityInfo.Identifier,
        Compiler.Symbol.TypeAlias t => t.V.EntityInfo.Identifier,
        _ => null,
    };
}
