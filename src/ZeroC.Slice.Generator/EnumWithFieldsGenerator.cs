// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Generates Dunet discriminated unions from Slice enums with fields.</summary>
internal static class EnumWithFieldsGenerator
{
    internal static CodeBlock Generate(EnumWithFields enumDef)
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
        EnumWithFields enumDef,
        string parentIdentifier,
        string accessModifier)
    {
        string enumName = enumDef.Name;
        return new ContainerBuilder(
                $"{accessModifier} partial record class",
                $"Unknown(int Discriminant, global::System.ReadOnlyMemory<byte> Fields)")
            .AddBase(parentIdentifier)
            .AddComment(
                "summary",
                @$"Represents an enumerator not defined in the local Slice definition of unchecked enum '{enumName}'.")
            .AddComment("param", "name", "Discriminant", "The discriminant of this unknown enumerator.")
            .AddComment("param", "name", "Fields", "The encoded fields of this unknown enumerator.")
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

    private static CodeBlock GenerateEncoderExtensions(EnumWithFields enumDef, string identifier, string accessModifier)
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
                        @$"The <see cref=""{identifier}"" /> enumerator value to encode.")
                    .SetBody("value.Encode(ref encoder)")
                    .Build())
            .Build();
    }

    private static CodeBlock GenerateUnionDeclaration(
        EnumWithFields enumDef,
        string identifier,
        string accessModifier,
        string currentNamespace)
    {
        string scopedId = enumDef.ScopedIdentifier;

        ContainerBuilder builder = new ContainerBuilder($"{accessModifier} abstract partial record class", identifier)
            .AddComment(
                "remarks",
                @$"The Slice compiler generated this discriminated union from the Slice enum <c>{scopedId}</c>.")
            .AddAttribute("Dunet.Union");

        // Generate nested record classes for each enumerator.
        foreach (EnumWithFields.Enumerator enumerator in enumDef.Enumerators)
        {
            builder.AddBlock(
                GenerateEnumeratorRecord(
                    enumerator,
                    enumDef,
                    identifier,
                    accessModifier,
                    currentNamespace,
                    enumerator.Discriminant));
        }

        // For unchecked enums, add the Unknown variant.
        if (enumDef.IsUnchecked)
        {
            builder.AddBlock(GenerateUnknownRecord(enumDef, identifier, accessModifier));
        }

        // Abstract Encode method.
        var abstractEncode = new CodeBlock();
        abstractEncode.WriteLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        abstractEncode.WriteLine("internal abstract void Encode(ref SliceEncoder encoder);");
        builder.AddBlock(abstractEncode);

        return builder.Build();
    }

    private static CodeBlock GenerateEnumeratorRecord(
        EnumWithFields.Enumerator enumerator,
        EnumWithFields enumDef,
        string parentIdentifier,
        string accessModifier,
        string currentNamespace,
        int discriminant)
    {
        string enumeratorName = enumerator.Name;

        // Build parameter list for the record constructor.
        string nameWithParams = enumerator.Fields.Count > 0
            ? $"{enumeratorName}({BuildParameterList(enumerator.Fields, "")})"
            : enumeratorName;

        return new ContainerBuilder($"{accessModifier} partial record class", nameWithParams)
            .AddBase(parentIdentifier)
            .AddCSAttributes(enumerator.Attributes)
            .AddBlock(
                $"""
                /// <summary>The discriminant of this enumerator, used for encoding/decoding.</summary>
                public const int Discriminant = {discriminant};
                """)
            .AddBlock(GenerateEncodeMethod(enumerator, enumDef, currentNamespace))
            .Build();
    }

    private static CodeBlock GenerateEncodeMethod(
        EnumWithFields.Enumerator enumerator,
        EnumWithFields enumDef,
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
        CodeBlock encodeBody = enumerator.Fields.GenerateEncodeBody(
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
        EnumWithFields enumDef,
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
                @$"The decoded <see cref=""{identifier}"" /> enumerator value.");

        var body = new CodeBlock();

        // Build the switch expression.
        body.WriteLine("return decoder.DecodeVarInt32() switch");
        body.WriteLine("{");
        foreach (EnumWithFields.Enumerator enumerator in enumDef.Enumerators)
        {
            string enumeratorName = enumerator.Name;
            body.WriteLine(
                $"    {identifier}.{enumeratorName}.Discriminant => Decode{enumeratorName}(ref decoder),");
        }

        // Fallback case.
        body.AddBlock(
            enumDef.IsUnchecked ?
                $"    int value => new {identifier}.Unknown(value, decoder.DecodeSequence<byte>())" :
                $$"""
                    int value => throw new global::System.IO.InvalidDataException(
                        $"Received invalid discriminant value '{value}' for {{identifier}}.")
                """);
        body.WriteLine("};");

        // Local static decode functions for each enumerator.
        foreach (EnumWithFields.Enumerator enumerator in enumDef.Enumerators)
        {
            body.AddBlock(GenerateDecodeLocalFunction(enumerator, enumDef, identifier, currentNamespace));
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
        EnumWithFields.Enumerator enumerator,
        EnumWithFields enumDef,
        string parentIdentifier,
        string currentNamespace)
    {
        string enumeratorName = enumerator.Name;
        IReadOnlyList<Field> sortedFields = enumerator.Fields.GetSortedFields();

        var code = new CodeBlock();
        code.WriteLine($"static {parentIdentifier}.{enumeratorName} Decode{enumeratorName}(ref SliceDecoder decoder)");
        code.WriteLine("{");

        // For unchecked enums, skip the size prefix.
        if (enumDef.IsUnchecked)
        {
            code.WriteLine("    decoder.SkipSize();");
        }

        // Bit sequence for non-tagged optional fields.
        int bitSequenceSize = enumerator.Fields.GetBitSequenceSize();
        if (bitSequenceSize > 0)
        {
            code.WriteLine($"    var bitSequenceReader = decoder.GetBitSequenceReader({bitSequenceSize});");
        }

        // Build the constructor call with named parameters.
        if (enumerator.Fields.Count == 0)
        {
            code.WriteLine($"    var result = new {parentIdentifier}.{enumeratorName}();");
        }
        else if (sortedFields.Count == 1 && !sortedFields[0].IsTagged)
        {
            // Single non-tagged field, simple one-liner.
            Field field = sortedFields[0];
            string paramName = field.Name;
            string decodeExpr = field.GetFieldDecodeExpression(currentNamespace);
            code.WriteLine($"    var result = new {parentIdentifier}.{enumeratorName}({paramName}: {decodeExpr});");
        }
        else
        {
            // Multi-field: build with named args.
            code.WriteLine($"    var result = new {parentIdentifier}.{enumeratorName}(");
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
