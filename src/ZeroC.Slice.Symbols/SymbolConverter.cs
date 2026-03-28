// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Converts decoded Slice compiler types into rich symbol types intended to use in code generators.</summary>
public sealed class SymbolConverter
{
    /// <summary>Converts source files into rich symbol types with all TypeRefs resolved. Reference files are used for
    /// type resolution but are not included in the output.</summary>
    internal static ImmutableList<SliceFile> ConvertFiles(
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
                    _named.TryAdd($"{moduleScope}::{id}", (file, symbol));
                }
            }
        }

        // Convert all named symbols in dependency order to fully populate _cache.
        foreach (string typeId in TopologicalSort())
        {
            (Compiler.SliceFile file, Compiler.Symbol symbol) = _named[typeId];
            Module module = ConvertModule(file.ModuleDeclaration);
            _cache[typeId] = ConvertSymbol(symbol, file, module);
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
                contents.Add(ResolveNamedSymbol($"{moduleScope}::{id}"));
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
        if (_cache.TryGetValue(typeId, out ISymbol? cached))
        {
            return cached;
        }

        // Fallback: convert on demand if the topological sort didn't pre-cache this type.
        (Compiler.SliceFile file, Compiler.Symbol symbol) = _named[typeId];
        Module module = ConvertModule(file.ModuleDeclaration);
        ISymbol converted = ConvertSymbol(symbol, file, module);
        _cache[typeId] = converted;
        return converted;
    }

    // Sorts all named symbols so dependencies appear before the symbols that use them.
    private List<string> TopologicalSort()
    {
        // Build dependency map: typeId -> set of named typeIds it depends on.
        var pending = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach ((string typeId, (Compiler.SliceFile file, Compiler.Symbol symbol)) in _named)
        {
            var deps = new HashSet<string>(StringComparer.Ordinal);
            CollectNamedTypeIds(symbol, file, deps);
            pending[typeId] = deps;
        }

        var sorted = new List<string>(_named.Count);
        var resolved = new HashSet<string>(StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            // Collect all entries whose deps are fully resolved.
            var ready = new List<string>();
            foreach ((string typeId, HashSet<string> deps) in pending)
            {
                if (deps.All(resolved.Contains))
                {
                    ready.Add(typeId);
                }
            }

            if (ready.Count == 0)
            {
                // Remaining types have unresolved deps — append them as-is.
                sorted.AddRange(pending.Keys);
                break;
            }

            foreach (string typeId in ready)
            {
                sorted.Add(typeId);
                resolved.Add(typeId);
                pending.Remove(typeId);
            }
        }

        return sorted;

        void CollectNamedTypeIds(Compiler.Symbol symbol, Compiler.SliceFile file, HashSet<string> result)
        {
            IEnumerable<string> typeIds = symbol switch
            {
                Compiler.Symbol.Struct structSymbol => StructDeps(structSymbol.V),
                Compiler.Symbol.TypeAlias typeAliasSymbol => TypeAliasDeps(typeAliasSymbol.V),
                Compiler.Symbol.Interface interfaceSymbol => InterfaceDeps(interfaceSymbol.V),
                Compiler.Symbol.BasicEnum basicEnumSymbol => BasicEnumDeps(basicEnumSymbol.V),
                Compiler.Symbol.VariantEnum variantEnumSymbol => VariantEnumDeps(variantEnumSymbol.V),
                Compiler.Symbol.SequenceType sequenceTypeSymbol => SequenceTypeDeps(sequenceTypeSymbol.V),
                Compiler.Symbol.DictionaryType dictionaryTypeSymbol => DictionaryTypeDeps(dictionaryTypeSymbol.V),
                Compiler.Symbol.ResultType resultTypeSymbol => ResultTypeDeps(resultTypeSymbol.V),
                _ => [],
            };

            foreach (string typeId in typeIds)
            {
                if (_named.ContainsKey(typeId))
                {
                    result.Add(typeId);
                }
                else if (int.TryParse(typeId, out int index))
                {
                    // Anonymous type — recurse to collect any named types it references.
                    CollectNamedTypeIds(file.Contents[index], file, result);
                }
                // else: builtin, ignore.
            }

            static IEnumerable<string> StructDeps(Compiler.Struct s) =>
                s.Fields.Select(f => f.DataType.TypeId);

            static IEnumerable<string> TypeAliasDeps(Compiler.TypeAlias t) =>
                [t.UnderlyingType.TypeId];

            static IEnumerable<string> SequenceTypeDeps(Compiler.SequenceType s) =>
                [s.ElementType.TypeId];

            static IEnumerable<string> DictionaryTypeDeps(Compiler.DictionaryType d) =>
                [d.KeyType.TypeId, d.ValueType.TypeId];

            static IEnumerable<string> ResultTypeDeps(Compiler.ResultType r) =>
                [r.SuccessType.TypeId, r.FailureType.TypeId];

            static IEnumerable<string> InterfaceDeps(Compiler.Interface i)
            {
                foreach (string baseId in i.Bases)
                {
                    yield return baseId;
                }

                foreach (Compiler.Operation op in i.Operations)
                {
                    foreach (Compiler.Field param in op.Parameters)
                    {
                        yield return param.DataType.TypeId;
                    }

                    foreach (Compiler.Field ret in op.ReturnType)
                    {
                        yield return ret.DataType.TypeId;
                    }
                }
            }

            static IEnumerable<string> BasicEnumDeps(Compiler.BasicEnum e) => [];

            static IEnumerable<string> VariantEnumDeps(Compiler.VariantEnum e) =>
                e.Variants.SelectMany(v => v.Fields.Select(f => f.DataType.TypeId));
        }
    }

    private ISymbol ConvertSymbol(Compiler.Symbol symbol, Compiler.SliceFile file, Module module) =>
        symbol switch
        {
            Compiler.Symbol.Struct s => ConvertStruct(s.V, file, module),
            Compiler.Symbol.BasicEnum e => ConvertBasicEnum(e.V, module),
            Compiler.Symbol.VariantEnum e => ConvertVariantEnum(e.V, file, module),
            Compiler.Symbol.Interface i => ConvertInterface(i.V, file, module),
            Compiler.Symbol.CustomType c => new CustomType
            {
                Identifier = c.V.EntityInfo.Identifier,
                Attributes = ConvertAttributes(c.V.EntityInfo.Attributes),
                Comment = ConvertComment(c.V.EntityInfo.Comment),
                Module = module,
            },
            Compiler.Symbol.TypeAlias t => new TypeAlias
            {
                Identifier = t.V.EntityInfo.Identifier,
                Attributes = ConvertAttributes(t.V.EntityInfo.Attributes),
                Comment = ConvertComment(t.V.EntityInfo.Comment),
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

    private Struct ConvertStruct(Compiler.Struct raw, Compiler.SliceFile file, Module module)
    {
        var result = new Struct
        {
            Identifier = raw.EntityInfo.Identifier,
            Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
            Comment = ConvertComment(raw.EntityInfo.Comment),
            Module = module,
            IsCompact = raw.IsCompact,
            Fields = raw.Fields.Select(f => ConvertField(f, file, module)).ToImmutableList(),
        };
        SetParent(result, result.Fields);
        return result;
    }

    private ISymbol ConvertBasicEnum(Compiler.BasicEnum raw, Module module)
    {
        Builtin builtin = _builtins[raw.Underlying];
        return builtin.Kind switch
        {
            BuiltinKind.Int8 => CreateBasicEnum(
                raw,
                module,
                builtin,
                (abs, isNegative) => isNegative ? (sbyte)-(long)abs : (sbyte)abs),
            BuiltinKind.UInt8 => CreateBasicEnum(
                raw,
                module,
                builtin,
                (abs, _) => (byte)abs),
            BuiltinKind.Int16 => CreateBasicEnum(
                raw,
                module,
                builtin,
                (abs, isNegative) => isNegative ? (short)-(long)abs : (short)abs),
            BuiltinKind.UInt16 => CreateBasicEnum(
                raw,
                module,
                builtin,
                (abs, _) => (ushort)abs),
            BuiltinKind.Int32 or BuiltinKind.VarInt32 => CreateBasicEnum(
                raw,
                module,
                builtin,
                (abs, isNegative) => isNegative ? (int)-(long)abs : (int)abs),
            BuiltinKind.UInt32 or BuiltinKind.VarUInt32 => CreateBasicEnum(
                raw,
                module,
                builtin,
                (abs, _) => (uint)abs),
            BuiltinKind.Int64 or BuiltinKind.VarInt62 => CreateBasicEnum(
                raw,
                module,
                builtin,
                (abs, isNegative) => isNegative ? -(long)abs : (long)abs),
            BuiltinKind.UInt64 or BuiltinKind.VarUInt62 => CreateBasicEnum(
                raw,
                module,
                builtin,
                (abs, _) => abs),
            _ => throw new InvalidOperationException(
                $"Unsupported enum underlying type: {builtin.Kind}"),
        };
    }

    private ISymbol ConvertVariantEnum(Compiler.VariantEnum raw, Compiler.SliceFile file, Module module)
    {
        var result = new VariantEnum
        {
            Identifier = raw.EntityInfo.Identifier,
            Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
            Comment = ConvertComment(raw.EntityInfo.Comment),
            Module = module,
            IsCompact = raw.IsCompact,
            IsUnchecked = raw.IsUnchecked,
            Variants = raw.Variants.Select(v => new VariantEnum.Variant
            {
                Identifier = v.EntityInfo.Identifier,
                Attributes = ConvertAttributes(v.EntityInfo.Attributes),
                Comment = ConvertComment(v.EntityInfo.Comment),
                Module = module,
                Discriminant = v.Discriminant,
                Fields = v.Fields.Select(f => ConvertField(f, file, module)).ToImmutableList(),
            }).ToImmutableList(),
        };
        SetParent(result, result.Variants);
        foreach (VariantEnum.Variant variant in result.Variants)
        {
            SetParent(variant, variant.Fields);
        }
        return result;
    }

    private Interface ConvertInterface(Compiler.Interface raw, Compiler.SliceFile file, Module module)
    {
        var result = new Interface
        {
            Identifier = raw.EntityInfo.Identifier,
            Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
            Comment = ConvertComment(raw.EntityInfo.Comment),
            Module = module,
            Bases = raw.Bases
                .Select(baseId => ResolveNamedSymbol(baseId))
                .OfType<Interface>()
                .ToImmutableList(),
            Operations = raw.Operations.Select(op => ConvertOperation(op, file, module)).ToImmutableList(),
        };
        SetParent(result, result.Operations);
        return result;
    }

    private Operation ConvertOperation(
        Compiler.Operation raw,
        Compiler.SliceFile file,
        Module module)
    {
        var result = new Operation
        {
            Identifier = raw.EntityInfo.Identifier,
            Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
            Comment = ConvertComment(raw.EntityInfo.Comment),
            Module = module,
            IsIdempotent = raw.IsIdempotent,
            Parameters = raw.Parameters.Select(f => ConvertField(f, file, module)).ToImmutableList(),
            HasStreamedParameter = raw.HasStreamedParameter,
            ReturnType = raw.ReturnType.Select(f => ConvertField(f, file, module)).ToImmutableList(),
            HasStreamedReturn = raw.HasStreamedReturn,
        };
        SetParent(result, result.Parameters);
        SetParent(result, result.ReturnType);
        return result;
    }

    private BasicEnum<T> CreateBasicEnum<T>(
        Compiler.BasicEnum raw,
        Module module,
        Builtin builtin,
        Func<ulong, bool, T> toValue) where T : struct, System.Numerics.INumber<T>
    {
        var result = new BasicEnum<T>
        {
            Identifier = raw.EntityInfo.Identifier,
            Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
            Comment = ConvertComment(raw.EntityInfo.Comment),
            Module = module,
            IsUnchecked = raw.IsUnchecked,
            Underlying = builtin,
            Enumerators = raw.Enumerators.Select(e => new BasicEnum<T>.Enumerator
            {
                Identifier = e.EntityInfo.Identifier,
                Attributes = ConvertAttributes(e.EntityInfo.Attributes),
                Comment = ConvertComment(e.EntityInfo.Comment),
                Module = module,
                Value = toValue(e.AbsoluteValue, e.HasNegativeValue),
            }).ToImmutableList(),
        };
        SetParent(result, result.Enumerators);
        return result;
    }

    private Field ConvertField(Compiler.Field raw, Compiler.SliceFile file, Module module) => new()
    {
        Identifier = raw.EntityInfo.Identifier,
        Attributes = ConvertAttributes(raw.EntityInfo.Attributes),
        Comment = ConvertComment(raw.EntityInfo.Comment),
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

    private static void SetParent(Entity parent, IEnumerable<Entity> children)
    {
        foreach (Entity child in children)
        {
            child.Parent = parent;
        }
    }

    private static ImmutableList<Attribute> ConvertAttributes(IList<Compiler.Attribute> raw) =>
        raw.Select(a => new Attribute
        {
            Directive = a.Directive,
            Args = [.. a.Args],
        }).ToImmutableList();

    private Comment? ConvertComment(Compiler.DocComment? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var overview = raw.Value.Overview.Select<Compiler.MessageComponent, CommentMessageComponent>(c => c switch
        {
            Compiler.MessageComponent.Text t => new CommentText(t.V),
            Compiler.MessageComponent.Link l => new CommentInlineLink(ResolveLink(l.V)),
            _ => throw new InvalidOperationException($"Unknown MessageComponent kind: {c.GetType().FullName}")
        }).ToImmutableList();

        var seeTags = raw.Value.SeeTags.Select(ResolveLink).ToImmutableList();

        return new Comment { Overview = overview, SeeTags = seeTags };

        CommentLink ResolveLink(string entityId) =>
            ResolveEntityById(entityId) is Entity entity
                ? new ResolvedCommentLink(entity)
                : new UnresolvedCommentLink(entityId);
    }

    private Entity? ResolveEntityById(string entityId)
    {
        // Try as top-level type first.
        if (_cache.TryGetValue(entityId, out ISymbol? symbol) && symbol is Entity entity)
        {
            return entity;
        }

        // Try as sub-entity: split off last segment, resolve parent, then find child.
        int lastSep = entityId.LastIndexOf("::", StringComparison.Ordinal);
        if (lastSep < 0)
        {
            return null;
        }

        string parentId = entityId[..lastSep];
        string childName = entityId[(lastSep + 2)..];

        Entity? parent = ResolveEntityById(parentId);
        return parent switch
        {
            Interface i => i.Operations.FirstOrDefault(o => o.Identifier == childName),
            VariantEnum ewf => ewf.Variants.FirstOrDefault(e => e.Identifier == childName),
            BasicEnum ewu => ewu.FindEnumeratorByIdentifier(childName),
            Struct s => s.Fields.FirstOrDefault(f => f.Identifier == childName),
            VariantEnum.Variant e => e.Fields.FirstOrDefault(f => f.Identifier == childName),
            Operation op => op.Parameters.Concat(op.ReturnType).FirstOrDefault(f => f.Identifier == childName),
            _ => null,
        };
    }

    private static string? GetNamedIdentifier(Compiler.Symbol symbol) => symbol switch
    {
        Compiler.Symbol.Struct s => s.V.EntityInfo.Identifier,
        Compiler.Symbol.BasicEnum e => e.V.EntityInfo.Identifier,
        Compiler.Symbol.VariantEnum e => e.V.EntityInfo.Identifier,
        Compiler.Symbol.Interface i => i.V.EntityInfo.Identifier,
        Compiler.Symbol.CustomType c => c.V.EntityInfo.Identifier,
        Compiler.Symbol.TypeAlias t => t.V.EntityInfo.Identifier,
        _ => null,
    };
}
