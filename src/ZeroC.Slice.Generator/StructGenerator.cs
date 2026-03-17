// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Generates C# record structs from Slice struct definitions.</summary>
internal static class StructGenerator
{
    internal static CodeBlock Generate(Struct structDef)
    {
        string identifier = structDef.Name;
        string currentNamespace = structDef.Namespace;
        string accessModifier = structDef.AccessModifier;
        bool isReadonly = structDef.Attributes.HasAttribute(CSAttributes.CSReadonly);

        // Build the declaration prefix.
        string declaration = isReadonly
            ? $"{accessModifier} readonly partial record struct"
            : $"{accessModifier} partial record struct";

        var builder = new ContainerBuilder(declaration, identifier);

        // Add doc comments.
        // TODO: format doc comment from structDef.Comment

        string scopedId = structDef.ScopedIdentifier;
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this record struct from the Slice struct <c>{scopedId}</c>.");

        // Add property declarations (in original order).
        var fieldDeclarations = CodeBlock.FromBlocks(
            structDef.Fields.Select(
                f => FieldDeclaration(f, currentNamespace, accessModifier, isReadonly)));
        builder.AddBlock(fieldDeclarations);

        // Add main constructor.
        builder.AddBlock(GenerateMainConstructor(structDef, currentNamespace, identifier, accessModifier));

        // Add decode constructor.
        builder.AddBlock(GenerateDecodeConstructor(structDef, currentNamespace, identifier, accessModifier));

        // Add encode method.
        builder.AddBlock(GenerateEncodeMethod(structDef, currentNamespace, accessModifier));

        return builder.Build();
    }

    private static CodeBlock GenerateMainConstructor(
        Struct structDef,
        string currentNamespace,
        string identifier,
        string accessModifier)
    {
        bool hasRequiredField = structDef.Fields.Any(f => f.IsRequired());

        var ctor = new FunctionBuilder(accessModifier, "", identifier, FunctionType.BlockBody);

        if (hasRequiredField)
        {
            ctor.AddSetsRequiredMembersAttribute();
        }

        ctor.AddComment("summary", @$"Constructs a new instance of <see cref=""{identifier}"" />.");

        foreach (Field field in structDef.Fields)
        {
            string typeString = field.DataType.FieldTypeString(field.DataTypeIsOptional, currentNamespace);
            string paramName = field.ParameterName;
            ctor.AddParameter(typeString, paramName);
        }

        var body = new CodeBlock();
        foreach (Field field in structDef.Fields)
        {
            body.WriteLine($"this.{field.Name} = {field.ParameterName};");
        }
        ctor.SetBody(body);

        return ctor.Build();
    }

    private static CodeBlock GenerateDecodeConstructor(
        Struct structDef,
        string currentNamespace,
        string identifier,
        string accessModifier)
    {
        IReadOnlyList<Field> sortedFields = structDef.Fields.GetSortedFields();
        bool hasRequiredField = structDef.Fields.Any(f => f.IsRequired());

        var ctor = new FunctionBuilder(accessModifier, "", identifier, FunctionType.BlockBody);

        if (hasRequiredField)
        {
            ctor.AddSetsRequiredMembersAttribute();
        }

        ctor.AddComment(
            "summary",
            @$"Constructs a new instance of <see cref=""{identifier}"" /> and decodes its fields from a Slice decoder.");
        ctor.AddComment("param", "name", "decoder", "The Slice decoder.");
        ctor.AddParameter("ref SliceDecoder", "decoder");

        var body = new CodeBlock();

        int bitSequenceSize = structDef.Fields.GetBitSequenceSize();
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceReader = decoder.GetBitSequenceReader({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            string fieldName = field.Name;
            string decodeExpr = field.GetFieldDecodeExpression(currentNamespace);
            body.WriteLine($"this.{fieldName} = {decodeExpr};");
        }

        if (!structDef.IsCompact)
        {
            body.WriteLine("decoder.SkipTagged();");
        }

        ctor.SetBody(body);
        return ctor.Build();
    }

    private static CodeBlock GenerateEncodeMethod(Struct structDef, string currentNamespace, string accessModifier)
    {
        IReadOnlyList<Field> sortedFields = structDef.Fields.GetSortedFields();

        var method = new FunctionBuilder($"{accessModifier} readonly", "void", "Encode", FunctionType.BlockBody);
        method.AddComment("summary", "Encodes the fields of this struct with a Slice encoder.");
        method.AddComment("param", "name", "encoder", "The Slice encoder.");
        method.AddParameter("ref SliceEncoder", "encoder");

        var body = new CodeBlock();

        int bitSequenceSize = structDef.Fields.GetBitSequenceSize();
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceWriter = encoder.GetBitSequenceWriter({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            if (field.IsTagged)
            {
                body.WriteLine(field.EncodeTaggedField(currentNamespace));
            }
            else if (field.DataTypeIsOptional)
            {
                // Non-tagged optional: write bit and encode conditionally.
                string param = $"this.{field.Name}";
                string valueParam = field.DataType.IsValueType() ? $"{param}.Value" : param;
                string encodeExpr = field.DataType.EncodeExpression(currentNamespace, valueParam);
                body.WriteLine($$"""
                    bitSequenceWriter.Write({param} != null);
                    if ({{param}} != null)
                    {
                        {{encodeExpr}};
                    }
                    """);
            }
            else
            {
                body.WriteLine(field.EncodeField(currentNamespace));
            }
        }

        if (!structDef.IsCompact)
        {
            body.WriteLine("encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
        }

        method.SetBody(body);
        return method.Build();
    }

    private static CodeBlock FieldDeclaration(
        Field field,
        string currentNamespace,
        string accessModifier,
        bool parentReadonly)
    {
        var code = new CodeBlock();

        // cs::attribute
        code.WriteCSAttributes(field.Attributes);

        string typeString = field.DataType.FieldTypeString(field.DataTypeIsOptional, currentNamespace);
        string fieldName = field.Name;
        string required = field.IsRequired() ? "required " : "";
        bool fieldReadonly = field.Attributes.HasAttribute(CSAttributes.CSReadonly);
        string accessor = (parentReadonly || fieldReadonly) ? "{ get; init; }" : "{ get; set; }";

        code.WriteLine($"{accessModifier} {required}{typeString} {fieldName} {accessor}");

        return code;
    }
}
