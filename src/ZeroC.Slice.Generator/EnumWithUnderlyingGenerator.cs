// Copyright (c) ZeroC, Inc.

using System.Globalization;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Generates C# enums and extension classes from Slice enum definitions.</summary>
internal sealed class EnumWithUnderlyingGenerator : Generator
{
    internal static CodeBlock Generate(EnumWithUnderlying enumDef) => enumDef.Underlying.Kind switch
    {
        BuiltinKind.Int8 => GenerateCore((EnumWithUnderlying<sbyte>)enumDef),
        BuiltinKind.UInt8 => GenerateCore((EnumWithUnderlying<byte>)enumDef),
        BuiltinKind.Int16 => GenerateCore((EnumWithUnderlying<short>)enumDef),
        BuiltinKind.UInt16 => GenerateCore((EnumWithUnderlying<ushort>)enumDef),
        BuiltinKind.Int32 or BuiltinKind.VarInt32 => GenerateCore((EnumWithUnderlying<int>)enumDef),
        BuiltinKind.UInt32 or BuiltinKind.VarUInt32 => GenerateCore((EnumWithUnderlying<uint>)enumDef),
        BuiltinKind.Int64 or BuiltinKind.VarInt62 => GenerateCore((EnumWithUnderlying<long>)enumDef),
        BuiltinKind.UInt64 or BuiltinKind.VarUInt62 => GenerateCore((EnumWithUnderlying<ulong>)enumDef),
        _ => throw new InvalidOperationException($"Unsupported enum underlying type: {enumDef.Underlying.Kind}"),
    };

    private static CodeBlock GenerateCore<T>(EnumWithUnderlying<T> enumDef) where T : struct, IFormattable
    {
        string identifier = enumDef.Name;
        string accessModifier = enumDef.AccessModifier;

        return CodeBlock.FromBlocks(
        [
            GenerateEnumDeclaration(enumDef, identifier, accessModifier),
            GenerateEnumUnderlyingExtensions(enumDef, identifier, accessModifier),
            GenerateEnumEncoderExtensions(enumDef, identifier, accessModifier),
            GenerateEnumDecoderExtensions(enumDef, identifier, accessModifier),
        ]);
    }

    private static CodeBlock GenerateEnumDeclaration<T>(
        EnumWithUnderlying<T> enumDef,
        string identifier,
        string accessModifier) where T : struct, IFormattable
    {
        var builder = new ContainerBuilder($"{accessModifier} enum", identifier);

        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this enum from the Slice enum <c>{enumDef.ScopedIdentifier}</c>.");

        // cs::attribute
        builder.AddCsAttributes(enumDef.Attributes);

        builder.AddBase(enumDef.Underlying.CsType());

        // Add enumerator declarations.
        foreach (EnumWithUnderlying<T>.Enumerator enumerator in enumDef.Enumerators)
        {
            var code = new CodeBlock();
            code.WriteCsAttributes(enumerator.Attributes);
            code.WriteLine($"{enumerator.Name} = {FormatValue(enumerator)},");
            builder.AddBlock(code);
        }
        return builder.Build();
    }

    private static CodeBlock GenerateEnumUnderlyingExtensions<T>(
        EnumWithUnderlying<T> enumDef,
        string identifier,
        string accessModifier) where T : struct, IFormattable
    {
        string csType = enumDef.Underlying.CsType();
        string csTypePascal = csType.ToPascalCase();
        string scopedId = enumDef.ScopedIdentifier;

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
            string values = string.Join(", ", enumDef.Enumerators.Select(FormatValue));
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
                string minValue = enumDef.Enumerators.Min(e => Convert.ToInt64(e.Value, CultureInfo.InvariantCulture))
                    .ToString(CultureInfo.InvariantCulture);
                string maxValue = enumDef.Enumerators.Max(e => Convert.ToInt64(e.Value, CultureInfo.InvariantCulture))
                    .ToString(CultureInfo.InvariantCulture);
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
        string csType = enumDef.Underlying.CsType();
        string suffix = enumDef.Underlying.Suffix();
        string scopedId = enumDef.ScopedIdentifier;

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
        string csType = enumDef.Underlying.CsType();
        string suffix = enumDef.Underlying.Suffix();
        string csTypePascal = csType.ToPascalCase();
        string scopedId = enumDef.ScopedIdentifier;

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

    private static string FormatValue<T>(EnumWithUnderlying<T>.Enumerator e) where T : struct, IFormattable =>
        e.Value.ToString(null, CultureInfo.InvariantCulture);

    private static string GetArticle(string word) =>
        word.Length > 0 && "aeiouAEIOU".Contains(word[0], StringComparison.Ordinal) ? "an" : "a";

    private static bool NeedsHashSetValidation<T>(EnumWithUnderlying<T> enumDef) where T : struct, IFormattable
    {
        if (enumDef.IsUnchecked || enumDef.Enumerators.Count == 0)
        {
            return false;
        }

        var values = enumDef.Enumerators.Select(e => Convert.ToInt64(e.Value, CultureInfo.InvariantCulture)).ToList();
        long min = values.Min();
        long max = values.Max();
        return enumDef.Enumerators.Count < (max - min + 1);
    }
}
