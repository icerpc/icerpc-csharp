// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Generates C# unions from Slice variant enums.</summary>
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

    private static CodeBlock GenerateUnknownRecord(VariantEnum enumDef, string accessModifier)
    {
        string enumName = enumDef.Name;
        return new ContainerBuilder(
                $"{accessModifier} sealed partial record class",
                $"Unknown(int Discriminant, global::System.ReadOnlyMemory<byte> Fields)")
            .AddComment(
                "summary",
                @$"Represents a variant not defined in the local Slice definition of unchecked enum '{enumName}'.")
            .AddComment("param", "name", "Discriminant", "The discriminant of this unknown variant.")
            .AddComment("param", "name", "Fields", "The encoded fields of this unknown variant.")
            .AddBlock($$"""
                /// <summary>Encodes this variant with a Slice encoder.</summary>
                /// <param name="encoder">The Slice encoder.</param>
                {{accessModifier}} void Encode(ref SliceEncoder encoder)
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

        var body = new CodeBlock();
        body.WriteLine("switch (value)");
        body.WriteLine("{");
        foreach (string caseName in CaseNames(enumDef))
        {
            body.WriteLine(
                $"""
                    case {identifier}.{caseName} variant:
                        variant.Encode(ref encoder);
                        break;
                """);
        }
        body.WriteLine(
            $"""
                case null:
                    throw new global::System.InvalidOperationException("Cannot encode a default {identifier}.");
            """);
        body.WriteLine("}");

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
                        FunctionType.BlockBody)
                    .AddComment("summary", @$"Encodes a <see cref=""{identifier}"" /> enum.")
                    .AddParameter("this ref SliceEncoder", "encoder", null, "The Slice encoder.")
                    .AddParameter(
                        identifier,
                        "value",
                        null,
                        @$"The <see cref=""{identifier}"" /> variant value to encode.")
                    .SetBody(body)
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

        string cases = string.Join(", ", CaseNames(enumDef).Select(caseName => $"{identifier}.{caseName}"));

        ContainerBuilder builder = new ContainerBuilder($"{accessModifier} partial union", $"{identifier}({cases})")
            .AddBase($"global::System.IEquatable<{identifier}>")
            .AddDocCommentSummary(enumDef.Comment, currentNamespace)
            .AddComment(
                "remarks",
                @$"The Slice compiler generated this union from the Slice enum <c>{scopedId}</c>.")
            .AddDocCommentSeeAlso(enumDef.Comment, currentNamespace)
            .AddDeprecatedAttribute(enumDef.Attributes);

        foreach (VariantEnum.Variant variant in enumDef.Variants)
        {
            builder.AddBlock(GenerateVariantRecord(variant, enumDef, accessModifier, currentNamespace));
        }

        if (enumDef.IsUnchecked)
        {
            builder.AddBlock(GenerateUnknownRecord(enumDef, accessModifier));
        }

        // A union is a plain struct, not a record: we generate the members a record struct would synthesize.
        builder.AddBlock(
            $$"""
            /// <inheritdoc/>
            public bool Equals({{identifier}} other) => Equals(Value, other.Value);

            /// <inheritdoc/>
            public override bool Equals(object? obj) => obj is {{identifier}} other && Equals(other);

            /// <inheritdoc/>
            public override int GetHashCode() => Value?.GetHashCode() ?? 0;

            /// <inheritdoc/>
            public override string ToString() => Value?.ToString() ?? "";

            /// <summary>Checks if two <see cref="{{identifier}}" /> values are equal.</summary>
            /// <param name="left">The first value.</param>
            /// <param name="right">The second value.</param>
            /// <returns><see langword="true" /> if the values are equal; otherwise, <see langword="false" />.</returns>
            public static bool operator ==({{identifier}} left, {{identifier}} right) => left.Equals(right);

            /// <summary>Checks if two <see cref="{{identifier}}" /> values are not equal.</summary>
            /// <param name="left">The first value.</param>
            /// <param name="right">The second value.</param>
            /// <returns><see langword="true" /> if the values differ; otherwise, <see langword="false" />.</returns>
            public static bool operator !=({{identifier}} left, {{identifier}} right) => !left.Equals(right);
            """);

        return builder.Build();
    }

    /// <summary>Returns the names of the nested case types of the union generated for a variant enum.</summary>
    private static IEnumerable<string> CaseNames(VariantEnum enumDef) =>
        enumDef.IsUnchecked ?
            enumDef.Variants.Select(variant => variant.Name).Append("Unknown") :
            enumDef.Variants.Select(variant => variant.Name);

    private static CodeBlock GenerateVariantRecord(
        VariantEnum.Variant variant,
        VariantEnum enumDef,
        string accessModifier,
        string currentNamespace)
    {
        ContainerBuilder builder = new ContainerBuilder($"{accessModifier} sealed partial record class", variant.Name)
            .AddDocCommentSummary(variant.Comment, currentNamespace);

        // Inside the union, the case record names can shadow type names from the enclosing namespace. We set
        // currentNamespace to "" to generate fully qualified type names for the parameters and the encode method.
        foreach (Field field in variant.Fields)
        {
            string type = field.DataType.FieldTypeString(field.DataTypeIsOptional, currentNamespace: "");
            builder.AddPrimaryConstructorParameter(
                PropertyAttributes(field) + type,
                field.Name,
                DocCommentFormatter.FormatOverview(field.Comment, currentNamespace));
        }

        return builder
            .AddDocCommentSeeAlso(variant.Comment, currentNamespace)
            .AddDeprecatedAttribute(variant.Attributes)
            .AddBlock(
                $"""
                /// <summary>The discriminant of this variant, used for encoding/decoding.</summary>
                {accessModifier} const int Discriminant = {variant.Discriminant};
                """)
            .AddBlock(GenerateEncodeMethod(variant, enumDef, accessModifier, currentNamespace: ""))
            .Build();
    }

    private static CodeBlock GenerateEncodeMethod(
        VariantEnum.Variant variant,
        VariantEnum enumDef,
        string accessModifier,
        string currentNamespace)
    {
        var code = new CodeBlock();
        code.WriteLine(
            $$"""
            /// <summary>Encodes this variant with a Slice encoder.</summary>
            /// <param name="encoder">The Slice encoder.</param>
            {{accessModifier}} void Encode(ref SliceEncoder encoder)
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

    /// <summary>Returns the <c>cs::attribute</c> and <c>deprecated</c> attributes of a variant field, with the
    /// <c>property:</c> target that applies them to the property the record generates for the parameter.</summary>
    private static string PropertyAttributes(Field field)
    {
        string attributes = string.Concat(
            field.Attributes.CSAttributes().Select(attr => $"[property: {attr.Args[0]}] "));
        if (field.Attributes.IsDeprecated)
        {
            attributes += field.Attributes.DeprecatedMessage is string message ?
                $"[property: global::System.Obsolete(\"{message}\")] " :
                "[property: global::System.Obsolete] ";
        }
        return attributes;
    }
}
