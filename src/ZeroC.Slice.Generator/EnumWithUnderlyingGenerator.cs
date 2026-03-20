// Copyright (c) ZeroC, Inc.

using System.Globalization;
using System.Numerics;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Generates C# enums and extension classes from Slice enum definitions.</summary>
internal static class EnumWithUnderlyingGenerator
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

    private static CodeBlock GenerateCore<T>(EnumWithUnderlying<T> enumDef) where T : struct, INumber<T>    {
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
        string accessModifier) where T : struct, INumber<T>
    {
        ContainerBuilder builder = new ContainerBuilder($"{accessModifier} enum", identifier)
            .AddComment(
                "remarks",
                $"The Slice compiler generated this enum from the Slice enum <c>{enumDef.ScopedIdentifier}</c>.")
            .AddCSAttributes(enumDef.Attributes)
            .AddBase(enumDef.Underlying.CSType);

        // Add enumerator declarations.
        foreach (EnumWithUnderlying<T>.Enumerator enumerator in enumDef.Enumerators)
        {
            var code = new CodeBlock();
            code.WriteCSAttributes(enumerator.Attributes);
            code.WriteLine($"{enumerator.Name} = {FormatValue(enumerator)},");
            builder.AddBlock(code);
        }
        return builder.Build();
    }

    private static CodeBlock GenerateEnumUnderlyingExtensions<T>(
        EnumWithUnderlying<T> enumDef,
        string identifier,
        string accessModifier) where T : struct, INumber<T>
    {
        string csType = enumDef.Underlying.CSType;
        string csTypePascal = csType.ToPascalCase();
        string scopedId = enumDef.ScopedIdentifier;

        ContainerBuilder builder = new ContainerBuilder(
                $"{accessModifier} static class",
                $"{identifier}{csTypePascal}Extensions")
            .AddComment(
                "summary",
                @$"Provides an extension method for creating {GetArticle(identifier)} <see cref=""{identifier}"" /> from {GetArticle(csType)} <see langword=""{csType}"" />.")
            .AddComment(
                "remarks",
                $"The Slice compiler generated this static class from the Slice enum <c>{scopedId}</c>.");

        bool useSet = NeedsHashSetValidation(enumDef);

        if (useSet)
        {
            string values = string.Join(", ", enumDef.Enumerators.Select(e => FormatValue(e)));
            var hashSetBlock = new CodeBlock();
            hashSetBlock.WriteLine(
                @$"private static readonly global::System.Collections.Generic.HashSet<{csType}> _enumeratorValues =
    new global::System.Collections.Generic.HashSet<{csType}> {{ {values} }};");
            builder.AddBlock(hashSetBlock);
        }

        // As{EnumName} method.
        FunctionBuilder method = new FunctionBuilder(
                $"{accessModifier} static",
                identifier,
                $"As{identifier}",
                FunctionType.ExpressionBody)
            .AddParameter($"this {csType}", "value", null, "The value being converted.")
            .AddComment(
                "summary",
                @$"Converts a <see langword=""{csType}"" /> into the corresponding <see cref=""{identifier}"" /> enumerator.")
            .AddComment("returns", "The enumerator.");

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
                string minValue = enumDef.Enumerators.Min(e => e.Value).ToString(null, CultureInfo.InvariantCulture);
                string maxValue = enumDef.Enumerators.Max(e => e.Value).ToString(null, CultureInfo.InvariantCulture);
                checkExpr = $"value is >= {minValue} and <= {maxValue}";
            }

            method.SetBody(
                $$"""
                {{checkExpr}} ?
                ({{identifier}})value :
                throw new global::System.IO.InvalidDataException($"Invalid enumerator value '{value}' for {{identifier}}.")
                """);

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
        string csType = enumDef.Underlying.CSType;
        string suffix = enumDef.Underlying.Suffix;
        string scopedId = enumDef.ScopedIdentifier;

        return new ContainerBuilder($"{accessModifier} static class", $"{identifier}SliceEncoderExtensions")
            .AddComment(
                "summary",
                @$"Provides an extension method for encoding a <see cref=""{identifier}"" /> using a <see cref=""SliceEncoder"" />.")
            .AddComment(
                "remarks",
                $"The Slice compiler generated this static class from the Slice enum " +
                $"<c>{scopedId}</c>.")
            .AddBlock(
                new FunctionBuilder(
                        $"{accessModifier} static",
                        "void",
                        $"Encode{identifier}",
                        FunctionType.ExpressionBody)
                    .AddComment("summary", @$"Encodes a <see cref=""{identifier}"" /> enum.")
                    .AddParameter("this ref SliceEncoder", "encoder", null, "The Slice encoder.")
                    .AddParameter(
                        identifier,
                        "value",
                        null,
                        @$"The <see cref=""{identifier}"" /> enumerator value to encode.")
                    .SetBody($"encoder.Encode{suffix}(({csType})value)")
                    .Build())
            .Build();
    }

    private static CodeBlock GenerateEnumDecoderExtensions(
        EnumWithUnderlying enumDef,
        string identifier,
        string accessModifier)
    {
        string csType = enumDef.Underlying.CSType;
        string suffix = enumDef.Underlying.Suffix;
        string csTypePascal = csType.ToPascalCase();
        string scopedId = enumDef.ScopedIdentifier;

        return new ContainerBuilder(
                $"{accessModifier} static class",
                $"{identifier}SliceDecoderExtensions")
            .AddComment(
                "summary",
                @$"Provides an extension method for decoding a <see cref=""{identifier}"" /> using a <see cref=""SliceDecoder"" />.")
            .AddComment(
                "remarks",
                $"The Slice compiler generated this static class from the Slice enum " +
                $"<c>{scopedId}</c>.")
            .AddBlock(
                new FunctionBuilder(
                        $"{accessModifier} static",
                        identifier,
                        $"Decode{identifier}",
                        FunctionType.ExpressionBody)
                    .AddComment("summary", @$"Decodes a <see cref=""{identifier}"" /> enum.")
                    .AddParameter("this ref SliceDecoder", "decoder", null, "The Slice decoder.")
                    .AddComment(
                        "returns",
                        @$"The decoded <see cref=""{identifier}"" /> enumerator value.")
                    .SetBody(
                        $"{identifier}{csTypePascal}Extensions.As{identifier}(decoder.Decode{suffix}())")
                    .Build())
            .Build();
    }

    private static string FormatValue<T>(EnumWithUnderlying<T>.Enumerator e) where T : struct, INumber<T> =>
        e.Value.ToString(null, CultureInfo.InvariantCulture);

    private static string GetArticle(string word) =>
        // cspell:disable-next-line
        word.Length > 0 && "aeiouAEIOU".Contains(word[0], StringComparison.Ordinal) ? "an" : "a";

    private static bool NeedsHashSetValidation<T>(EnumWithUnderlying<T> enumDef) where T : struct, INumber<T>    {
        // If the enumerator count covers the full range of the underlying type, every value is valid
        // and no validation is needed. This also prevents overflow when computing max - min below
        // for small types like sbyte where MaxValue - MinValue (127 - (-128) = 255) overflows.
        int? bitSize = enumDef.Underlying.Kind switch
        {
            BuiltinKind.Int8 or BuiltinKind.UInt8 => 8,
            BuiltinKind.Int16 or BuiltinKind.UInt16 => 16,
            _ => null, // int32 and larger: count (an int) can never cover the full range
        };

        if (enumDef.IsUnchecked ||
            enumDef.Enumerators.Count == 0 ||
            (bitSize is int bits && enumDef.Enumerators.Count >= (1 << bits)))
        {
            return false;
        }

        T min = enumDef.Enumerators.Min(e => e.Value);
        T max = enumDef.Enumerators.Max(e => e.Value);
        return T.CreateChecked(enumDef.Enumerators.Count - 1) < max - min;
    }
}
