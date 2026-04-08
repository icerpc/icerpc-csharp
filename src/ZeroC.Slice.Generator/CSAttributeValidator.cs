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
            ValidateAttributes(file.Attributes, Target.File, diagnostics);
            ValidateAttributes(file.Module.Attributes, Target.Module, diagnostics);

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
                ValidateAttributes(s.Attributes, Target.Struct, diagnostics);
                foreach (Field field in s.Fields)
                {
                    ValidateField(field, Target.FieldInStruct, diagnostics);
                }
                break;

            case VariantEnum e:
                ValidateAttributes(e.Attributes, Target.VariantEnum, diagnostics);
                foreach (VariantEnum.Variant en in e.Variants)
                {
                    ValidateAttributes(en.Attributes, Target.Variant, diagnostics);
                    foreach (Field field in en.Fields)
                    {
                        ValidateField(field, Target.Field, diagnostics);
                    }
                }
                break;

            case BasicEnum e:
                ValidateAttributes(e.Attributes, Target.BasicEnum, diagnostics);
                ValidateEnumerators(e, diagnostics);
                break;

            case Interface i:
                ValidateAttributes(i.Attributes, Target.Interface, diagnostics);
                foreach (Operation op in i.Operations)
                {
                    ValidateOperation(op, diagnostics);
                }
                break;

            case CustomType c:
                ValidateAttributes(c.Attributes, Target.CustomType, diagnostics);
                if (!c.Attributes.HasAttribute(CSAttributes.CSType))
                {
                    diagnostics.Add(Error(
                        $"Custom type '{c.Identifier}' is missing required attribute '{CSAttributes.CSType}'."));
                }
                break;

            case TypeAlias t:
                ValidateAttributes(t.Attributes, Target.TypeAlias, diagnostics);
                ValidateTypeRef(t.UnderlyingType, diagnostics);
                break;
        }
    }

    private static void ValidateEnumerators(
        BasicEnum e,
        List<Diagnostic> diagnostics)
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

        static void ValidateEnumerators<T>(
            BasicEnum<T> e,
            List<Diagnostic> diagnostics)
            where T : struct, System.Numerics.INumber<T>
        {
            foreach (BasicEnum<T>.Enumerator en in e.Enumerators)
            {
                ValidateAttributes(en.Attributes, Target.Enumerator, diagnostics);
            }
        }
    }

    private static void ValidateOperation(Operation op, List<Diagnostic> diagnostics)
    {
        ValidateAttributes(op.Attributes, Target.Operation, diagnostics);

        foreach (Field param in op.Parameters)
        {
            ValidateField(param, Target.Field, diagnostics);
        }

        foreach (Field ret in op.ReturnType)
        {
            ValidateField(ret, Target.Field, diagnostics);
        }

        // cs::encodedReturn: only valid on operations with non-streamed return values.
        if (op.Attributes.HasAttribute(CSAttributes.CSEncodedReturn))
        {
            bool hasNonStreamedReturn = op.ReturnType.Count > (op.HasStreamedReturn ? 1 : 0);
            if (!hasNonStreamedReturn)
            {
                string reason = op.HasStreamedReturn
                    ? "an operation that only returns a stream"
                    : "an operation that does not return anything";
                diagnostics.Add(Error(
                    $"The '{CSAttributes.CSEncodedReturn}' attribute is not applicable to {reason}."));
            }
        }
    }

    private static void ValidateField(Field field, Target target, List<Diagnostic> diagnostics)
    {
        ValidateAttributes(field.Attributes, target, diagnostics);
        ValidateTypeRef(field.DataType, diagnostics);
    }

    private static void ValidateTypeRef(TypeRef typeRef, List<Diagnostic> diagnostics)
    {
        Target target = typeRef.Type switch
        {
            SequenceType => Target.TypeRefSequence,
            DictionaryType => Target.TypeRefDictionary,
            _ => Target.TypeRef,
        };
        ValidateAttributes(typeRef.Attributes, target, diagnostics);
    }

    private static void ValidateAttributes(
        ImmutableList<Attribute> attributes,
        Target target,
        List<Diagnostic> diagnostics)
    {
        foreach (Attribute attr in attributes)
        {
            if (attr.Directive.StartsWith("cs::", StringComparison.Ordinal))
            {
                ValidateCSAttribute(attr, target, diagnostics);
            }
        }
    }

    private static void ValidateCSAttribute(
        Attribute attr,
        Target target,
        List<Diagnostic> diagnostics)
    {
        switch (attr.Directive)
        {
            case CSAttributes.CSAttribute:
                // The cs::attribute attribute is only allow for C# construction were adding attributes using a partial
                // declaration is not possible,such as on enumerators and fields.
                RequireArgs(attr, 1, diagnostics);
                if (target is not (Target.BasicEnum or Target.Enumerator or Target.Field or Target.FieldInStruct))
                {
                    ReportUnexpected(attr, diagnostics);
                }
                break;

            case CSAttributes.CSEncodedReturn:
                RequireArgs(attr, 0, diagnostics);
                if (target is not Target.Operation)
                {
                    ReportUnexpected(attr, diagnostics);
                }
                // Additional semantic validation (non-streamed returns) is in ValidateOperation.
                break;

            case CSAttributes.CSIdentifier:
                RequireArgs(attr, 1, diagnostics);
                if (target is Target.Module)
                {
                    diagnostics.Add(Error(
                        $"Unexpected attribute '{attr.Directive}' on module. " +
                        $"To map a module to a different C# namespace, use '{CSAttributes.CSNamespace}' instead."));
                }
                else if (target is Target.File or Target.TypeAlias or Target.TypeRef
                    or Target.TypeRefSequence or Target.TypeRefDictionary)
                {
                    ReportUnexpected(attr, diagnostics);
                }
                break;

            case CSAttributes.CSNamespace:
                RequireArgs(attr, 1, diagnostics);
                if (target is not Target.Module)
                {
                    ReportUnexpected(attr, diagnostics);
                }
                break;

            case CSAttributes.CSPublic:
                RequireArgs(attr, 0, diagnostics);
                if (target is not (Target.Struct or Target.Interface or Target.BasicEnum or Target.VariantEnum))
                {
                    ReportUnexpected(attr, diagnostics);
                }
                break;

            case CSAttributes.CSReadonly:
                RequireArgs(attr, 0, diagnostics);
                if (target is not (Target.Struct or Target.FieldInStruct))
                {
                    if (target is Target.Field)
                    {
                        diagnostics.Add(Error(
                            $"Unexpected attribute '{attr.Directive}'. " +
                            "'cs::readonly' can only be applied to structs, or fields inside structs."));
                    }
                    else
                    {
                        ReportUnexpected(attr, diagnostics);
                    }
                }
                break;

            case CSAttributes.CSType:
                RequireArgs(attr, 1, diagnostics);
                if (target is not (Target.CustomType or Target.TypeRefSequence or Target.TypeRefDictionary))
                {
                    diagnostics.Add(Error(
                        $"Unexpected attribute '{attr.Directive}'. " +
                        "The cs::type attribute can only be applied to sequences, dictionaries, and custom types."));
                }
                break;

            default:
                diagnostics.Add(Error($"Unknown CS attribute '{attr.Directive}'."));
                break;
        }
    }

    private static void RequireArgs(Attribute attr, int expected, List<Diagnostic> diagnostics)
    {
        if (attr.Args.Count != expected)
        {
            diagnostics.Add(Error(
                $"Attribute '{attr.Directive}' requires {expected} argument(s) but got {attr.Args.Count}."));
        }
    }

    private static void ReportUnexpected(Attribute attr, List<Diagnostic> diagnostics) =>
        diagnostics.Add(Error($"Unexpected attribute '{attr.Directive}' on this target."));

    private static Diagnostic Error(string message) =>
        new() { Level = DiagnosticLevel.Error, Message = message };
}
