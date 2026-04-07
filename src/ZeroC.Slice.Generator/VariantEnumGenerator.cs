// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Generates Dunet discriminated unions from Slice variant enums.</summary>
internal static class VariantEnumGenerator
{
    internal static CodeBlock Generate(VariantEnum enumDef)
    {
        string identifier = enumDef.Name;
        string accessModifier = enumDef.AccessModifier;
        string currentNamespace = enumDef.Namespace;

        return CodeBlock.FromBlocks(
        [
            GenerateUnionDeclaration(enumDef, identifier, accessModifier, currentNamespace),
            GenerateEncoderExtensions(enumDef, identifier, accessModifier),
            GenerateDecoderExtensions(enumDef, identifier, accessModifier, currentNamespace),
        ]);
    }

    private static CodeBlock GenerateUnknownRecord(
        VariantEnum enumDef,
        string parentIdentifier)
    {
        string enumName = enumDef.Name;
        return new ContainerBuilder(
                "partial record class",
                $"Unknown(int Discriminant, global::System.ReadOnlyMemory<byte> Fields)")
            .AddBase(parentIdentifier)
            .AddComment(
                "summary",
                @$"Represents a variant not defined in the local Slice definition of unchecked enum '{enumName}'.")
            .AddComment("param", "name", "Discriminant", "The discriminant of this unknown variant.")
            .AddComment("param", "name", "Fields", "The encoded fields of this unknown variant.")
            .AddBlock("""
                [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
                internal override void Encode(ref SliceEncoder encoder)
                {
                    encoder.EncodeVarInt32(Discriminant);
                    encoder.EncodeSize(Fields.Length);
                    encoder.WriteByteSpan(Fields.Span);
                }
                """)
            .Build();
    }

    private static CodeBlock GenerateEncoderExtensions(VariantEnum enumDef, string identifier, string accessModifier)
    {
        string scopedId = enumDef.ScopedIdentifier;

        return new ContainerBuilder($"{accessModifier} static class", $"{identifier}SliceEncoderExtensions")
            .AddComment(
                "summary",
                @$"Provides an extension method for encoding a <see cref=""{identifier}"" /> using a <see cref=""SliceEncoder"" />.")
            .AddComment(
                "remarks",
                $"The Slice compiler generated this static class from the Slice enum <c>{scopedId}</c>.")
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
                        @$"The <see cref=""{identifier}"" /> variant value to encode.")
                    .SetBody("value.Encode(ref encoder)")
                    .Build())
            .Build();
    }

    private static CodeBlock GenerateUnionDeclaration(
        VariantEnum enumDef,
        string identifier,
        string accessModifier,
        string currentNamespace)
    {
        string scopedId = enumDef.ScopedIdentifier;

        ContainerBuilder builder = new ContainerBuilder($"{accessModifier} abstract partial record class", identifier)
            .AddDocCommentSummary(enumDef.Comment, currentNamespace)
            .AddComment(
                "remarks",
                @$"The Slice compiler generated this discriminated union from the Slice enum <c>{scopedId}</c>.")
            .AddDocCommentSeeAlso(enumDef.Comment, currentNamespace)
            .AddDeprecatedAttribute(enumDef.Attributes)
            .AddAttribute("Dunet.Union");

        // Generate nested record classes for each variant.
        foreach (VariantEnum.Variant variant in enumDef.Variants)
        {
            builder.AddBlock(
                GenerateVariantRecord(
                    variant,
                    enumDef,
                    identifier,
                    currentNamespace,
                    variant.Discriminant));
        }

        // For unchecked variant enums, add the Unknown variant.
        if (enumDef.IsUnchecked)
        {
            builder.AddBlock(GenerateUnknownRecord(enumDef, identifier));
        }

        // Abstract Encode method.
        var abstractEncode = new CodeBlock();
        abstractEncode.WriteLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        abstractEncode.WriteLine("internal abstract void Encode(ref SliceEncoder encoder);");
        builder.AddBlock(abstractEncode);

        return builder.Build();
    }

    private static CodeBlock GenerateVariantRecord(
        VariantEnum.Variant variant,
        VariantEnum enumDef,
        string parentIdentifier,
        string currentNamespace,
        int discriminant)
    {
        string variantName = variant.Name;

        // Build parameter list for the record constructor.
        string nameWithParams = variant.Fields.Count > 0
            ? $"{variantName}({BuildParameterList(variant.Fields, "")})"
            : variantName;

        return new ContainerBuilder("partial record class", nameWithParams)
            .AddDocCommentSummary(variant.Comment, currentNamespace)
            .AddDocCommentSeeAlso(variant.Comment, currentNamespace)
            .AddDeprecatedAttribute(variant.Attributes)
            .AddBase(parentIdentifier)
            .AddCSAttributes(variant.Attributes)
            .AddBlock(
                $"""
                /// <summary>The discriminant of this variant, used for encoding/decoding.</summary>
                public const int Discriminant = {discriminant};
                """)
            .AddBlock(GenerateEncodeMethod(variant, enumDef, currentNamespace))
            .Build();
    }

    private static CodeBlock GenerateEncodeMethod(
        VariantEnum.Variant variant,
        VariantEnum enumDef,
        string currentNamespace)
    {
        var code = new CodeBlock();
        code.WriteLine(
            """
            [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
            internal override void Encode(ref SliceEncoder encoder)
            {
                encoder.EncodeVarInt32(Discriminant);
            """);

        // For unchecked (non-compact) enums, add size placeholder.
        if (enumDef.IsUnchecked)
        {
            code.WriteLine(
                """
                    var sizePlaceholder = encoder.GetPlaceholderSpan(4);
                    int startPos = encoder.EncodedByteCount;
                """);
        }

        // Encode fields (bit sequence, tagged, optional, regular, and tag end marker).
        CodeBlock encodeBody = variant.Fields.GenerateEncodeBody(
            currentNamespace,
            includeTagEndMarker: !enumDef.IsCompact);
        code.WriteLine($"    {encodeBody.Indent()}");

        // Close size for unchecked enums.
        if (enumDef.IsUnchecked)
        {
            code.WriteLine("    SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);");
        }

        code.WriteLine("}");
        return code;
    }

    private static CodeBlock GenerateDecoderExtensions(
        VariantEnum enumDef,
        string identifier,
        string accessModifier,
        string currentNamespace)
    {
        string scopedId = enumDef.ScopedIdentifier;

        FunctionBuilder method = new FunctionBuilder(
                $"{accessModifier} static",
                identifier,
                $"Decode{identifier}",
                FunctionType.BlockBody)
            .AddComment("summary", @$"Decodes a <see cref=""{identifier}"" /> enum.")
            .AddParameter("this ref SliceDecoder", "decoder", null, "The Slice decoder.")
            .AddComment(
                "returns",
                @$"The decoded <see cref=""{identifier}"" /> variant value.");

        var body = new CodeBlock();

        // Build the switch expression.
        body.WriteLine("return decoder.DecodeVarInt32() switch");
        body.WriteLine("{");
        foreach (VariantEnum.Variant variant in enumDef.Variants)
        {
            string variantName = variant.Name;
            body.WriteLine(
                $"    {identifier}.{variantName}.Discriminant => Decode{variantName}(ref decoder),");
        }

        // Fallback case.
        if (enumDef.IsUnchecked)
        {
            body.WriteLine(
                $"    int value => new {identifier}.Unknown(value, decoder.DecodeSequence<byte>())");
        }
        else
        {
            body.WriteLine(
                $$"""
                      int value => throw new global::System.IO.InvalidDataException(
                          $"Received invalid discriminant value '{value}' for {{scopedId}}.")
                  """);
        }
        body.WriteLine("};");

        // Local static decode functions for each variant.
        foreach (VariantEnum.Variant variant in enumDef.Variants)
        {
            body.AddBlock(GenerateDecodeLocalFunction(variant, enumDef, identifier, currentNamespace));
        }

        method.SetBody(body);

        return new ContainerBuilder(
                $"{accessModifier} static class",
                $"{identifier}SliceDecoderExtensions")
            .AddComment(
                "summary",
                @$"Provides an extension method for decoding a <see cref=""{identifier}"" /> using a <see cref=""SliceDecoder"" />.")
            .AddComment(
                "remarks",
                $"The Slice compiler generated this static class from the Slice enum <c>{scopedId}</c>.")
            .AddBlock(method.Build())
            .Build();
    }

    private static CodeBlock GenerateDecodeLocalFunction(
        VariantEnum.Variant variant,
        VariantEnum enumDef,
        string parentIdentifier,
        string currentNamespace)
    {
        string variantName = variant.Name;
        IReadOnlyList<Field> sortedFields = variant.Fields.GetSortedFields();

        var code = new CodeBlock();
        code.WriteLine($"static {parentIdentifier}.{variantName} Decode{variantName}(ref SliceDecoder decoder)");
        code.WriteLine("{");

        // For unchecked enums, skip the size prefix.
        if (enumDef.IsUnchecked)
        {
            code.WriteLine("    decoder.SkipSize();");
        }

        // Bit sequence for non-tagged optional fields.
        int bitSequenceSize = variant.Fields.GetBitSequenceSize();
        if (bitSequenceSize > 0)
        {
            code.WriteLine($"    var bitSequenceReader = decoder.GetBitSequenceReader({bitSequenceSize});");
        }

        // Build the constructor call with named parameters.
        if (variant.Fields.Count == 0)
        {
            code.WriteLine($"    var result = new {parentIdentifier}.{variantName}();");
        }
        else if (sortedFields.Count == 1 && !sortedFields[0].IsTagged)
        {
            // Single non-tagged field, simple one-liner.
            Field field = sortedFields[0];
            string paramName = field.Name;
            string decodeExpr = field.GetFieldDecodeExpression(currentNamespace);
            code.WriteLine($"    var result = new {parentIdentifier}.{variantName}({paramName}: {decodeExpr});");
        }
        else
        {
            // Multi-field: build with named args.
            code.WriteLine($"    var result = new {parentIdentifier}.{variantName}(");
            for (int i = 0; i < sortedFields.Count; i++)
            {
                Field field = sortedFields[i];
                string paramName = field.Name;
                string decodeExpr = field.GetFieldDecodeExpression(currentNamespace);
                string separator = i < sortedFields.Count - 1 ? "," : ");";
                code.WriteLine($"        {paramName}: {decodeExpr}{separator}");
            }
        }

        // Skip tagged fields for non-compact enums.
        if (!enumDef.IsCompact)
        {
            code.WriteLine("    decoder.SkipTagged();");
        }

        code.WriteLine("    return result;");
        code.WriteLine("}");

        return code;
    }

    private static string BuildParameterList(ImmutableList<Field> fields, string currentNamespace)
    {
        return string.Join(", ", fields.Select(f =>
        {
            string typeString = f.DataType.FieldTypeString(f.DataTypeIsOptional, currentNamespace);
            string paramName = f.Name;
            return $"{typeString} {paramName}";
        }));
    }
}
