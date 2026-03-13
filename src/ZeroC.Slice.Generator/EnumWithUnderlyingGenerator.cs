// Copyright (c) ZeroC, Inc.

using System.Globalization;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Generates C# enums and extension classes from Slice enum definitions.</summary>
internal sealed class EnumWithUnderlyingGenerator : Generator
{
    internal static CodeBlock Generate(EnumWithUnderlying enumDef)
    {
        string identifier = enumDef.EntityInfo.Name;
        string accessModifier = AccessModifier(enumDef.EntityInfo);

        return CodeBlock.FromBlocks(
        [
            GenerateEnumDeclaration(enumDef, identifier, accessModifier),
            GenerateEnumUnderlyingExtensions(enumDef, identifier, accessModifier),
            GenerateEnumEncoderExtensions(enumDef, identifier, accessModifier),
            GenerateEnumDecoderExtensions(enumDef, identifier, accessModifier),
        ]);
    }

    private static CodeBlock GenerateEnumDeclaration(
        EnumWithUnderlying enumDef,
        string identifier,
        string accessModifier)
    {
        var builder = new ContainerBuilder($"{accessModifier} enum", identifier);

        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this enum from the Slice enum <c>{enumDef.EntityInfo.ScopedSliceId}</c>.");

        // cs::attribute
        builder.AddCsAttributes(enumDef.EntityInfo.Attributes);

        // [System.Flags] for unchecked enums.
        if (enumDef.IsUnchecked)
        {
            builder.AddAttribute("System.Flags");
        }

        builder.AddBase(enumDef.Underlying.CsType);

        // Add enumerator declarations.
        foreach (EnumWithUnderlying.Enumerator enumerator in enumDef.Enumerators)
        {
            var code = new CodeBlock();
            code.WriteCsAttributes(enumerator.EntityInfo.Attributes);
            code.WriteLine($"{enumerator.EntityInfo.Name} = {EnumeratorValue(enumerator)},");
            builder.AddBlock(code);
        }
        return builder.Build();
    }

    private static CodeBlock GenerateEnumUnderlyingExtensions(
        EnumWithUnderlying enumDef,
        string identifier,
        string accessModifier)
    {
        string csType = enumDef.Underlying.CsType;
        string csTypePascal = csType.ToPascalCase();
        string scopedId = enumDef.EntityInfo.ScopedSliceId;

        var builder = new ContainerBuilder($"{accessModifier} static class", $"{identifier}{csTypePascal}Extensions");

        builder.AddComment(
            "summary",
            @$"Provides an extension method for creating {GetArticle(identifier)} <see cref=""{identifier}"" /> from {GetArticle(csType)} <see langword=""{csType}"" />.");
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice enum <c>{scopedId}</c>.");

        bool useSet = NeedsHashSetValidation(enumDef);

        if (useSet)
        {
            string values = string.Join(", ", enumDef.Enumerators.Select(EnumeratorValue));
            var hashSetBlock = new CodeBlock();
            hashSetBlock.WriteLine(
                @$"private static readonly global::System.Collections.Generic.HashSet<{csType}> _enumeratorValues =
    new global::System.Collections.Generic.HashSet<{csType}> {{ {values} }};");
            builder.AddBlock(hashSetBlock);
        }

        // As{EnumName} method.
        var method = new FunctionBuilder(
            $"{accessModifier} static",
            identifier,
            $"As{identifier}",
            FunctionType.ExpressionBody);

        method.AddParameter($"this {csType}", "value", null, "The value being converted.");
        method.AddComment(
            "summary",
            @$"Converts a <see langword=""{csType}"" /> into the corresponding <see cref=""{identifier}"" /> enumerator.");
        method.AddComment("returns", "The enumerator.");

        if (enumDef.IsUnchecked || enumDef.Enumerators.Count == 0)
        {
            method.SetBody($"({identifier})value");
        }
        else
        {
            string checkExpr;
            if (useSet)
            {
                checkExpr = "_enumeratorValues.Contains(value)";
            }
            else
            {
                string minValue = enumDef.Enumerators.Select(SignedValue).Min().ToString(CultureInfo.InvariantCulture);
                string maxValue = enumDef.Enumerators.Select(SignedValue).Max().ToString(CultureInfo.InvariantCulture);
                checkExpr = $"value is >= {minValue} and <= {maxValue}";
            }

            method.SetBody(
                @$"{checkExpr} ?
({identifier})value :
throw new global::System.IO.InvalidDataException($""Invalid enumerator value '{{value}}' for {identifier}."")");

            method.AddComment(
                "exception",
                "cref",
                "global::System.IO.InvalidDataException",
                "Thrown when the value does not correspond to one of the enumerators.");
        }

        builder.AddBlock(method.Build());
        return builder.Build();
    }

    private static CodeBlock GenerateEnumEncoderExtensions(
        EnumWithUnderlying enumDef,
        string identifier,
        string accessModifier)
    {
        string csType = enumDef.Underlying.CsType;
        string suffix = enumDef.Underlying.Suffix;
        string scopedId = enumDef.EntityInfo.ScopedSliceId;

        var builder = new ContainerBuilder($"{accessModifier} static class", $"{identifier}SliceEncoderExtensions");

        builder.AddComment(
            "summary",
            @$"Provides an extension method for encoding a <see cref=""{identifier}"" /> using a <see cref=""SliceEncoder"" />.");
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice enum " +
            $"<c>{scopedId}</c>.");

        var method = new FunctionBuilder(
            $"{accessModifier} static",
            "void",
            $"Encode{identifier}",
            FunctionType.ExpressionBody);

        method.AddComment("summary", @$"Encodes a <see cref=""{identifier}"" /> enum.");
        method.AddParameter("this ref SliceEncoder", "encoder", null, "The Slice encoder.");
        method.AddParameter(
            identifier,
            "value",
            null,
            @$"The <see cref=""{identifier}"" /> enumerator value to encode.");

        method.SetBody($"encoder.Encode{suffix}(({csType})value)");

        builder.AddBlock(method.Build());
        return builder.Build();
    }

    private static CodeBlock GenerateEnumDecoderExtensions(
        EnumWithUnderlying enumDef,
        string identifier,
        string accessModifier)
    {
        string csType = enumDef.Underlying.CsType;
        string suffix = enumDef.Underlying.Suffix;
        string csTypePascal = csType.ToPascalCase();
        string scopedId = enumDef.EntityInfo.ScopedSliceId;

        var builder = new ContainerBuilder(
            $"{accessModifier} static class",
            $"{identifier}SliceDecoderExtensions");

        builder.AddComment(
            "summary",
            @$"Provides an extension method for decoding a <see cref=""{identifier}"" /> using a <see cref=""SliceDecoder"" />.");
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice enum " +
            $"<c>{scopedId}</c>.");

        var method = new FunctionBuilder(
            $"{accessModifier} static",
            identifier,
            $"Decode{identifier}",
            FunctionType.ExpressionBody);

        method.AddComment("summary", @$"Decodes a <see cref=""{identifier}"" /> enum.");
        method.AddParameter("this ref SliceDecoder", "decoder", null, "The Slice decoder.");
        method.AddComment(
            "returns",
            @$"The decoded <see cref=""{identifier}"" /> enumerator value.");

        method.SetBody(
            $"{identifier}{csTypePascal}Extensions.As{identifier}(decoder.Decode{suffix}())");

        builder.AddBlock(method.Build());
        return builder.Build();
    }

    private static string EnumeratorValue(EnumWithUnderlying.Enumerator e) =>
        e.IsPositive
            ? e.AbsoluteValue.ToString(CultureInfo.InvariantCulture)
            : $"-{e.AbsoluteValue.ToString(CultureInfo.InvariantCulture)}";

    private static string GetArticle(string word) =>
        word.Length > 0 && "aeiouAEIOU".Contains(word[0], StringComparison.Ordinal) ? "an" : "a";

    private static bool NeedsHashSetValidation(EnumWithUnderlying enumDef)
    {
        if (enumDef.IsUnchecked || enumDef.Enumerators.Count == 0)
        {
            return false;
        }

        var values = enumDef.Enumerators.Select(SignedValue).ToList();
        long min = values.Min();
        long max = values.Max();
        return enumDef.Enumerators.Count < (max - min + 1);
    }

    private static long SignedValue(EnumWithUnderlying.Enumerator e) =>
        e.IsPositive ? (long)e.AbsoluteValue : -(long)e.AbsoluteValue;
}
