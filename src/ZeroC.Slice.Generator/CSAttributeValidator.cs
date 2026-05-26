// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.Slice.Symbols;

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Validates that all CS-specific attributes are correctly used.</summary>
internal static class CsAttributeValidator
{
    private enum Target
    {
        File,
        Module,
        Struct,
        Interface,
        BasicEnum,
        VariantEnum,
        Enumerator,
        Variant,
        Field,
        FieldInStruct,
        Operation,
        CustomType,
        TypeAlias,
        TypeRef,
        TypeRefSequence,
        TypeRefDictionary,
    }

    /// <summary>Validates all CS attributes across the given files and returns any diagnostics.</summary>
    internal static List<Diagnostic> Validate(ImmutableList<SliceFile> files)
    {
        var diagnostics = new List<Diagnostic>();

        foreach (SliceFile file in files)
        {
            ValidateAttributes(file.Attributes, $"#{file.Path}", Target.File, diagnostics);
            ValidateAttributes(file.Module.Attributes, file.Module.Identifier, Target.Module, diagnostics);

            foreach (ISymbol symbol in file.Contents)
            {
                ValidateSymbol(symbol, diagnostics);
            }
        }

        return diagnostics;
    }

    private static void ValidateSymbol(ISymbol symbol, List<Diagnostic> diagnostics)
    {
        switch (symbol)
        {
            case Struct s:
                ValidateAttributes(s.Attributes, s.ScopedIdentifier, Target.Struct, diagnostics);
                foreach (Field field in s.Fields)
                {
                    ValidateField(field, Target.FieldInStruct, diagnostics);
                }
                break;

            case VariantEnum e:
                ValidateAttributes(e.Attributes, e.ScopedIdentifier, Target.VariantEnum, diagnostics);
                foreach (VariantEnum.Variant en in e.Variants)
                {
                    ValidateAttributes(en.Attributes, en.ScopedIdentifier, Target.Variant, diagnostics);
                    foreach (Field field in en.Fields)
                    {
                        ValidateField(field, Target.Field, diagnostics);
                    }
                }
                break;

            case BasicEnum e:
                ValidateAttributes(e.Attributes, e.ScopedIdentifier, Target.BasicEnum, diagnostics);
                ValidateEnumerators(e, diagnostics);
                break;

            case Interface i:
                ValidateAttributes(i.Attributes, i.ScopedIdentifier, Target.Interface, diagnostics);
                foreach (Operation op in i.Operations)
                {
                    ValidateOperation(op, diagnostics);
                }
                break;

            case CustomType c:
                ValidateAttributes(c.Attributes, c.ScopedIdentifier, Target.CustomType, diagnostics);
                if (!c.Attributes.HasAttribute(CSAttributes.CSType))
                {
                    var diagnostic = Diagnostic.MissingRequiredAttribute("cs::type(TYPE_STRING)", c.ScopedIdentifier);
                    diagnostic.AddNote("custom types must have their type specified with a 'cs::type' attribute");
                    diagnostics.Add(diagnostic);
                }
                break;

            case TypeAlias t:
                ValidateAttributes(t.Attributes, t.ScopedIdentifier, Target.TypeAlias, diagnostics);
                ValidateTypeRef(t.UnderlyingType, t.ScopedIdentifier, diagnostics);
                break;
        }
    }

    private static void ValidateEnumerators(BasicEnum e, List<Diagnostic> diagnostics)
    {
        switch (e)
        {
            case BasicEnum<sbyte> basicEnum:
                ValidateEnumerators(basicEnum, diagnostics);
                break;
            case BasicEnum<byte> basicEnum:
                ValidateEnumerators(basicEnum, diagnostics);
                break;
            case BasicEnum<short> basicEnum:
                ValidateEnumerators(basicEnum, diagnostics);
                break;
            case BasicEnum<ushort> basicEnum:
                ValidateEnumerators(basicEnum, diagnostics);
                break;
            case BasicEnum<int> basicEnum:
                ValidateEnumerators(basicEnum, diagnostics);
                break;
            case BasicEnum<uint> basicEnum:
                ValidateEnumerators(basicEnum, diagnostics);
                break;
            case BasicEnum<long> basicEnum:
                ValidateEnumerators(basicEnum, diagnostics);
                break;
            case BasicEnum<ulong> basicEnum:
                ValidateEnumerators(basicEnum, diagnostics);
                break;
        }

        static void ValidateEnumerators<T>(BasicEnum<T> e, List<Diagnostic> diagnostics)
            where T : struct, System.Numerics.INumber<T>
        {
            foreach (BasicEnum<T>.Enumerator en in e.Enumerators)
            {
                ValidateAttributes(en.Attributes, en.ScopedIdentifier, Target.Enumerator, diagnostics);
            }
        }
    }

    private static void ValidateOperation(Operation op, List<Diagnostic> diagnostics)
    {
        ValidateAttributes(op.Attributes, op.ScopedIdentifier, Target.Operation, diagnostics);

        foreach (Field param in op.Parameters)
        {
            ValidateField(param, Target.Field, diagnostics);
        }

        foreach (Field ret in op.ReturnType)
        {
            ValidateField(ret, Target.Field, diagnostics);
        }

        // cs::encodedReturn: only valid on operations with non-streamed return values.
        bool hasNonStreamedReturn = op.ReturnType.Count > (op.HasStreamedReturn ? 1 : 0);
        if (!hasNonStreamedReturn)
        {
            for (int i = 0; i < op.Attributes.Count; i++)
            {
                if (op.Attributes[i].Directive == CSAttributes.CSEncodedReturn)
                {
                    string source = $"{op.ScopedIdentifier}::$attributes::{i}";
                    var diagnostic = Diagnostic.InvalidAttribute(CSAttributes.CSEncodedReturn, source);
                    diagnostic.AddNote($"'{CSAttributes.CSEncodedReturn}' cannot be applied to an operation that " +
                        (op.HasStreamedReturn ? "only returns a stream" : "does not return anything"));
                    diagnostics.Add(diagnostic);
                }
            }
        }
    }

    private static void ValidateField(Field field, Target target, List<Diagnostic> diagnostics)
    {
        ValidateAttributes(field.Attributes, field.ScopedIdentifier, target, diagnostics);
        ValidateTypeRef(field.DataType, field.ScopedIdentifier, diagnostics);
    }

    private static void ValidateTypeRef(TypeRef typeRef, string source, List<Diagnostic> diagnostics)
    {
        Target target = typeRef.Type switch
        {
            SequenceType => Target.TypeRefSequence,
            DictionaryType => Target.TypeRefDictionary,
            _ => Target.TypeRef,
        };
        ValidateAttributes(typeRef.Attributes, source, target, diagnostics);
    }

    private static void ValidateAttributes(
        ImmutableList<Attribute> attributes,
        string source,
        Target target,
        List<Diagnostic> diagnostics)
    {
        for (int i = 0; i < attributes.Count; i++)
        {
            if (attributes[i].Directive.StartsWith("cs::", StringComparison.Ordinal))
            {
                string attrSource = $"{source}::$attributes::{i}";
                ValidateCSAttribute(attributes[i], attrSource, target, diagnostics);
            }
        }
    }

    private static void ValidateCSAttribute(
        Attribute attr,
        string source,
        Target target,
        List<Diagnostic> diagnostics)
    {
        switch (attr.Directive)
        {
            case CSAttributes.CSAttribute:
                // The cs::attribute attribute is only allowed for C# constructs where adding attributes using a partial
                // declaration is not possible, such as enumerators and fields.
                RequireArgs(attr, 1, source, diagnostics);
                if (target is not (Target.BasicEnum or Target.Enumerator or Target.Field or Target.FieldInStruct))
                {
                    var diagnostic = Diagnostic.InvalidAttribute(CSAttributes.CSAttribute, source);
                    diagnostic.AddNote("'cs::attribute' can only be applied to basic enums, enumerators, and fields");
                    diagnostic.AddNote("consider applying the attribute directly in C# using a partial declaration");
                    diagnostics.Add(diagnostic);
                }
                break;

            case CSAttributes.CSEncodedReturn:
                RequireArgs(attr, 0, source, diagnostics);
                if (target is not Target.Operation)
                {
                    var diagnostic = Diagnostic.InvalidAttribute(CSAttributes.CSEncodedReturn, source);
                    diagnostic.AddNote("'cs::encodedReturn' can only be applied to operations");
                    diagnostics.Add(diagnostic);
                }
                // Additional semantic validation (non-streamed returns) is in ValidateOperation.
                break;

            case CSAttributes.CSIdentifier:
                RequireArgs(attr, 1, source, diagnostics);
                if (target is Target.File or Target.TypeAlias or Target.TypeRef
                    or Target.TypeRefSequence or Target.TypeRefDictionary)
                {
                    var diagnostic = Diagnostic.InvalidAttribute(CSAttributes.CSIdentifier, source);
                    diagnostic.AddNote("'cs::identifier' can only be applied to Slice elements which have identifiers");
                    diagnostics.Add(diagnostic);
                }
                break;

            case CSAttributes.CSPublic:
                RequireArgs(attr, 0, source, diagnostics);
                if (target is not (Target.Struct or Target.Interface or Target.BasicEnum or Target.VariantEnum))
                {
                    var diagnostic = Diagnostic.InvalidAttribute(CSAttributes.CSPublic, source);
                    diagnostic.AddNote("'cs::public' can only be applied to structs, interfaces, and enums");
                    diagnostics.Add(diagnostic);
                }
                break;

            case CSAttributes.CSReadonly:
                RequireArgs(attr, 0, source, diagnostics);
                if (target is not (Target.Struct or Target.FieldInStruct))
                {
                    var diagnostic = Diagnostic.InvalidAttribute(CSAttributes.CSReadonly, source);
                    diagnostic.AddNote("'cs::readonly' can only be applied to structs or fields inside structs");
                    diagnostics.Add(diagnostic);
                }
                break;

            case CSAttributes.CSType:
                RequireArgs(attr, 1, source, diagnostics);
                if (target is not (Target.CustomType or Target.TypeRefSequence or Target.TypeRefDictionary))
                {
                    var diagnostic = Diagnostic.InvalidAttribute(CSAttributes.CSType, source);
                    diagnostic.AddNote("'cs::type' can only be applied to sequences, dictionaries, and custom types");
                    diagnostics.Add(diagnostic);
                }
                break;

            default:
                diagnostics.Add(Diagnostic.UnknownAttribute(attr.Directive, source));
                break;
        }
    }

    private static void RequireArgs(Attribute attr, int expected, string source, List<Diagnostic> diagnostics)
    {
        if (attr.Args.Count != expected)
        {
            diagnostics.Add(
                Diagnostic.IncorrectAttributeArgumentCount(attr.Directive, expected, attr.Args.Count, source));
        }
    }
}
