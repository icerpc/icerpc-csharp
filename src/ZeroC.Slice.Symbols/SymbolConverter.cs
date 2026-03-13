// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Converts decoded Slice compiler types into rich symbol types intended to use in code generators.
/// </summary>
public sealed class SymbolConverter
{
    /// <summary>
    /// Converts source files into rich symbol types with all TypeRefs resolved. Reference files are used
    /// for type resolution but are not included in the output.
    /// </summary>
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
    private readonly Dictionary<string, Symbol> _cache = new(StringComparer.Ordinal);

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

        var contents = ImmutableList.CreateBuilder<Symbol>();
        for (int i = 0; i < file.Contents.Count; i++)
        {
            Compiler.Symbol raw = file.Contents[i];
            string? id = GetNamedIdentifier(raw);

            if (id is not null)
            {
                // Named type — resolve through cache using its scoped TypeId.
                string key = string.IsNullOrEmpty(moduleScope) ? id : $"{moduleScope}::{id}";
                contents.Add(ResolveNamedType(key));
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

    /// <summary>Resolves a TypeId to a Symbol, handling primitives, anonymous types, and named types.</summary>
    private Symbol ResolveTypeId(string typeId, Compiler.SliceFile currentFile)
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
            return ConvertSymbol(currentFile.Contents[index], currentFile, module);
        }

        // Named type.
        return ResolveNamedType(typeId);
    }

    private Symbol ResolveNamedType(string typeId)
    {
        if (_cache.TryGetValue(typeId, out Symbol? cached))
        {
            return cached;
        }

        // Try exact match first, then progressively strip leading module components. The slicec compiler
        // emits only the leaf module component in Module.Identifier, but TypeRef TypeIds use the full
        // module path, so "ZeroC::Slice::CodeGen::Tests::CustomType" won't match key "Tests::CustomType"
        // without this fallback.
        string lookupKey = typeId;
        while (!_named.ContainsKey(lookupKey))
        {
            int sep = lookupKey.IndexOf("::", StringComparison.Ordinal);
            if (sep < 0)
            {
                throw new InvalidOperationException($"Failed to resolve named type with ID '{typeId}'.");
            }
            lookupKey = lookupKey[(sep + 2)..];
        }

        (Compiler.SliceFile file, Compiler.Symbol symbol) = _named[lookupKey];
        Module module = ConvertModule(file.ModuleDeclaration);
        Symbol converted = ConvertSymbol(symbol, file, module);
        _cache[typeId] = converted;
        return converted;
    }

    private Symbol ConvertSymbol(Compiler.Symbol symbol, Compiler.SliceFile file, Module module) =>
        symbol switch
        {
            Compiler.Symbol.Struct s => ConvertStruct(s.V, file, module),
            Compiler.Symbol.Enum e => ConvertEnum(e.V, file, module),
            Compiler.Symbol.Interface i => ConvertInterface(i.V, file, module),
            Compiler.Symbol.CustomType c => new CustomType
            {
                EntityInfo = ConvertEntityInfo(c.V.EntityInfo, module),
            },
            Compiler.Symbol.TypeAlias t => new TypeAlias
            {
                EntityInfo = ConvertEntityInfo(t.V.EntityInfo, module),
                UnderlyingType = ConvertTypeRef(t.V.UnderlyingType, file),
            },
            Compiler.Symbol.SequenceType s => new SequenceType
            {
                ElementType = ConvertTypeRef(s.V.ElementType, file),
            },
            Compiler.Symbol.DictionaryType d => new DictionaryType
            {
                KeyType = ConvertTypeRef(d.V.KeyType, file),
                ValueType = ConvertTypeRef(d.V.ValueType, file),
            },
            Compiler.Symbol.ResultType r => new ResultType
            {
                SuccessType = ConvertTypeRef(r.V.SuccessType, file),
                FailureType = ConvertTypeRef(r.V.FailureType, file),
            },
            _ => throw new InvalidOperationException($"Unknown symbol type: {symbol.GetType().Name}"),
        };

    private Struct ConvertStruct(Compiler.Struct raw, Compiler.SliceFile file, Module module) => new()
    {
        EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
        IsCompact = raw.IsCompact,
        Fields = raw.Fields.Select(f => ConvertField(f, file, module)).ToImmutableList(),
    };

    private Symbol ConvertEnum(Compiler.Enum raw, Compiler.SliceFile file, Module module)
    {
        if (raw.Underlying is string u && _builtins.TryGetValue(u, out var builtin))
        {
            return new EnumWithUnderlying
            {
                EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
                IsUnchecked = raw.IsUnchecked,
                Underlying = builtin,
                Enumerators = raw.Enumerators.Select(e => new EnumWithUnderlying.Enumerator
                {
                    EntityInfo = ConvertEntityInfo(e.EntityInfo, module),
                    AbsoluteValue = e.Value.AbsoluteValue,
                    IsPositive = e.Value.IsPositive,
                }).ToImmutableList(),
            };
        }
        else
        {
            return new EnumWithFields
            {
                EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
                IsCompact = raw.IsCompact,
                IsUnchecked = raw.IsUnchecked,
                Enumerators = raw.Enumerators.Select(e => new EnumWithFields.Enumerator
                {
                    EntityInfo = ConvertEntityInfo(e.EntityInfo, module),
                    Discriminant = (int)e.Value.AbsoluteValue,
                    Fields = e.Fields.Select(f => ConvertField(f, file, module)).ToImmutableList(),
                }).ToImmutableList(),
            };
        }
    }

    private Interface ConvertInterface(Compiler.Interface raw, Compiler.SliceFile file, Module module) =>
        new()
        {
            EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
            Bases = raw.Bases
            .Select(baseId => ResolveNamedType(baseId))
            .OfType<Interface>()
            .ToImmutableList(),
            Operations = raw.Operations.Select(op => ConvertOperation(op, file, module)).ToImmutableList(),
        };

    private Operation ConvertOperation(
        Compiler.Operation raw,
        Compiler.SliceFile file,
        Module module) => new()
        {
            EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
            IsIdempotent = raw.IsIdempotent,
            Parameters = raw.Parameters.Select(f => ConvertField(f, file, module)).ToImmutableList(),
            HasStreamedParameter = raw.HasStreamedParameter,
            ReturnType = raw.ReturnType.Select(f => ConvertField(f, file, module)).ToImmutableList(),
            HasStreamedReturn = raw.HasStreamedReturn,
        };

    private Field ConvertField(Compiler.Field raw, Compiler.SliceFile file, Module module) => new()
    {
        EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
        Tag = raw.Tag,
        Type = ConvertTypeRef(raw.DataType, file),
    };

    private TypeRef ConvertTypeRef(Compiler.TypeRef raw, Compiler.SliceFile file) => new()
    {
        Symbol = ResolveTypeId(raw.TypeId, file),
        IsOptional = raw.IsOptional,
        Attributes = ConvertAttributes(raw.TypeAttributes),
    };

    private static EntityInfo ConvertEntityInfo(Compiler.EntityInfo raw, Module module) => new()
    {
        Identifier = raw.Identifier,
        Attributes = ConvertAttributes(raw.Attributes),
        Module = module,
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
