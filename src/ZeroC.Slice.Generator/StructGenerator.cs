// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;
using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Generates C# record structs from Slice struct definitions.</summary>
internal sealed class StructGenerator : Generator
{
    internal CodeBlock Generate(Struct structDef)
    {
        string identifier = structDef.EntityInfo.Name;
        string currentNamespace = structDef.EntityInfo.Namespace;
        string accessModifier = AccessModifier(structDef.EntityInfo);
        bool isReadonly = structDef.EntityInfo.Attributes.HasAttribute(Attribute.CsReadonly);

        // Build the declaration prefix.
        string declaration = isReadonly
            ? $"{accessModifier} readonly partial record struct"
            : $"{accessModifier} partial record struct";

        var builder = new ContainerBuilder(declaration, identifier);

        // Add doc comments.
        // TODO: format doc comment from structDef.EntityInfo.Comment

        string scopedId = structDef.EntityInfo.ScopedSliceId;
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

    private CodeBlock GenerateMainConstructor(
        Struct structDef,
        string currentNamespace,
        string identifier,
        string accessModifier)
    {
        bool hasRequiredField = structDef.Fields.Any(f => f.IsRequired);

        var ctor = new FunctionBuilder(accessModifier, "", identifier, FunctionType.BlockBody);

        if (hasRequiredField)
        {
            ctor.AddSetsRequiredMembersAttribute();
        }

        ctor.AddComment("summary", @$"Constructs a new instance of <see cref=""{identifier}"" />.");

        foreach (Field field in structDef.Fields)
        {
            string typeString = FieldTypeString(field.Type, currentNamespace);
            string paramName = field.EntityInfo.ParameterName;
            ctor.AddParameter(typeString, paramName);
        }

        var body = new CodeBlock();
        foreach (Field field in structDef.Fields)
        {
            body.WriteLine($"this.{field.EntityInfo.Name} = {field.EntityInfo.ParameterName};");
        }
        ctor.SetBody(body);

        return ctor.Build();
    }

    private CodeBlock GenerateDecodeConstructor(
        Struct structDef,
        string currentNamespace,
        string identifier,
        string accessModifier)
    {
        IReadOnlyList<Field> sortedFields = GetSortedFields(structDef.Fields);
        bool hasRequiredField = structDef.Fields.Any(f => f.IsRequired);

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

        int bitSequenceSize = GetBitSequenceSize(structDef.Fields);
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceReader = decoder.GetBitSequenceReader({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            string fieldName = field.EntityInfo.Name;
            string decodeExpr = GetFieldDecodeExpression(field, currentNamespace);
            body.WriteLine($"this.{fieldName} = {decodeExpr};");
        }

        if (!structDef.IsCompact)
        {
            body.WriteLine("decoder.SkipTagged();");
        }

        ctor.SetBody(body);
        return ctor.Build();
    }

    private CodeBlock GenerateEncodeMethod(Struct structDef, string currentNamespace, string accessModifier)
    {
        IReadOnlyList<Field> sortedFields = GetSortedFields(structDef.Fields);

        var method = new FunctionBuilder($"{accessModifier} readonly", "void", "Encode", FunctionType.BlockBody);
        method.AddComment("summary", "Encodes the fields of this struct with a Slice encoder.");
        method.AddComment("param", "name", "encoder", "The Slice encoder.");
        method.AddParameter("ref SliceEncoder", "encoder");

        var body = new CodeBlock();

        int bitSequenceSize = GetBitSequenceSize(structDef.Fields);
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceWriter = encoder.GetBitSequenceWriter({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            if (field.IsTagged)
            {
                body.WriteLine(EncodeTaggedField(field, currentNamespace));
            }
            else if (field.Type.IsOptional)
            {
                // Non-tagged optional: write bit and encode conditionally.
                string param = $"this.{field.EntityInfo.Name}";
                string valueParam = field.Type.IsValueType ? $"{param}.Value" : param;
                string encodeExpr = EncodeExpression(field.Type, currentNamespace, valueParam);
                body.WriteLine($$"""
                    bitSequenceWriter.Write({param} != null);
                    if ({{param}} != null)
                    {
                        {{encodeExpr}}
                    }
                    """);
            }
            else
            {
                body.WriteLine(EncodeField(field, currentNamespace));
            }
        }

        if (!structDef.IsCompact)
        {
            body.WriteLine("encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
        }

        method.SetBody(body);
        return method.Build();
    }
}
