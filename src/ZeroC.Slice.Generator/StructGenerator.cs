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

        string scopedId = structDef.ScopedIdentifier;

        return new ContainerBuilder(declaration, identifier)
            .AddDocCommentSummary(structDef.Comment, currentNamespace)
            .AddComment(
                "remarks",
                $"The Slice compiler generated this record struct from the Slice struct <c>{scopedId}</c>.")
            .AddDocCommentSeeAlso(structDef.Comment, currentNamespace)
            .AddBlock(CodeBlock.FromBlocks(
                structDef.Fields.Select(
                    f => FieldDeclaration(f, currentNamespace, accessModifier, isReadonly))))
            .AddBlock(GenerateMainConstructor(structDef, currentNamespace, identifier, accessModifier))
            .AddBlock(GenerateDecodeConstructor(structDef, currentNamespace, identifier, accessModifier))
            .AddBlock(GenerateEncodeMethod(structDef, currentNamespace, accessModifier))
            .Build();
    }

    private static CodeBlock GenerateMainConstructor(
        Struct structDef,
        string currentNamespace,
        string identifier,
        string accessModifier)
    {
        bool hasRequiredField = structDef.Fields.Any(f => f.IsRequired);

        FunctionBuilder ctor = new FunctionBuilder(accessModifier, "", identifier, FunctionType.BlockBody)
            .AddComment("summary", @$"Constructs a new instance of <see cref=""{identifier}"" />.");

        if (hasRequiredField)
        {
            ctor.AddSetsRequiredMembersAttribute();
        }

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
        bool hasRequiredField = structDef.Fields.Any(f => f.IsRequired);

        FunctionBuilder ctor = new FunctionBuilder(accessModifier, "", identifier, FunctionType.BlockBody)
            .AddComment(
                "summary",
                @$"Constructs a new instance of <see cref=""{identifier}"" /> and decodes its fields from a Slice decoder.")
            .AddComment("param", "name", "decoder", "The Slice decoder.")
            .AddParameter("ref SliceDecoder", "decoder");

        if (hasRequiredField)
        {
            ctor.AddSetsRequiredMembersAttribute();
        }

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

    private static CodeBlock GenerateEncodeMethod(Struct structDef, string currentNamespace, string accessModifier) =>
        new FunctionBuilder($"{accessModifier} readonly", "void", "Encode", FunctionType.BlockBody)
            .AddComment("summary", "Encodes the fields of this struct with a Slice encoder.")
            .AddComment("param", "name", "encoder", "The Slice encoder.")
            .AddParameter("ref SliceEncoder", "encoder")
            .SetBody(structDef.Fields.GenerateEncodeBody(
                currentNamespace,
                includeTagEndMarker: !structDef.IsCompact))
            .Build();


    private static CodeBlock FieldDeclaration(
        Field field,
        string currentNamespace,
        string accessModifier,
        bool parentReadonly)
    {
        var code = new CodeBlock();

        code.WriteDocCommentSummary(field.Comment, currentNamespace);

        // cs::attribute
        code.WriteCSAttributes(field.Attributes);

        string typeString = field.DataType.FieldTypeString(field.DataTypeIsOptional, currentNamespace);
        string fieldName = field.Name;
        string required = field.IsRequired ? "required " : "";
        bool fieldReadonly = field.IsReadonly;
        string accessor = (parentReadonly || fieldReadonly) ? "{ get; init; }" : "{ get; set; }";

        code.WriteLine($"{accessModifier} {required}{typeString} {fieldName} {accessor}");

        return code;
    }
}
