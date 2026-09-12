// Copyright (c) ZeroC, Inc.

namespace ZeroC.CodeBuilder;

/// <summary>A builder for creating C# container types (classes, structs, records, interfaces).</summary>
public sealed class ContainerBuilder : IBuilder, IAttributeBuilder<ContainerBuilder>, ICommentBuilder<ContainerBuilder>
{
    private readonly List<string> _attributes = [];
    private readonly List<string> _bases = [];
    private readonly List<CommentTag> _comments = [];
    private readonly string _containerType;
    private readonly List<CodeBlock> _contents = [];
    private readonly List<string> _parameters = [];
    private readonly string _name;

    /// <summary>Initializes a new instance of the <see cref="ContainerBuilder"/> class.</summary>
    /// <param name="containerType">The container type (e.g., "class", "struct", "record", "interface").</param>
    /// <param name="name">The name of the container.</param>
    public ContainerBuilder(string containerType, string name)
    {
        _containerType = containerType;
        _name = name;
    }

    /// <inheritdoc/>
    public ContainerBuilder AddAttribute(string attribute)
    {
        _attributes.Add(attribute);
        return this;
    }

    /// <summary>Adds a base type or interface to the container.</summary>
    /// <param name="baseType">The base type or interface name.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public ContainerBuilder AddBase(string baseType)
    {
        _bases.Add(baseType);
        return this;
    }

    /// <summary>Adds multiple base types or interfaces to the container.</summary>
    /// <param name="bases">The base types or interface names.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public ContainerBuilder AddBases(IEnumerable<string> bases)
    {
        _bases.AddRange(bases);
        return this;
    }

    /// <summary>Adds a code block to the container body.</summary>
    /// <param name="content">The code block to add.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public ContainerBuilder AddBlock(CodeBlock content)
    {
        _contents.Add(content);
        return this;
    }

    /// <inheritdoc/>
    public ContainerBuilder AddComment(string tag, string content)
    {
        _comments.Add(new CommentTag(tag, content));
        return this;
    }

    /// <inheritdoc/>
    public ContainerBuilder AddComment(string tag, string attributeName, string attributeValue, string content)
    {
        _comments.Add(new CommentTag(tag, attributeName, attributeValue, content));
        return this;
    }

    /// <inheritdoc/>
    public ContainerBuilder AddComments(IEnumerable<CommentTag> comments)
    {
        _comments.AddRange(comments);
        return this;
    }

    /// <summary>Adds a parameter to the primary constructor of the container.</summary>
    /// <param name="paramType">The parameter type, including any attributes.</param>
    /// <param name="paramName">The parameter name.</param>
    /// <param name="docComment">An optional documentation comment.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public ContainerBuilder AddPrimaryConstructorParameter(
        string paramType,
        string paramName,
        string? docComment = null)
    {
        if (docComment is not null)
        {
            AddComment("param", "name", paramName.TrimStart('@'), docComment);
        }
        _parameters.Add($"{paramType} {paramName}");
        return this;
    }

    /// <inheritdoc/>
    public CodeBlock Build()
    {
        var code = new CodeBlock();

        foreach (CommentTag comment in _comments)
        {
            code.WriteLine(comment.ToString());
        }

        foreach (string attribute in _attributes)
        {
            code.WriteLine($"[{attribute}]");
        }

        string basesClause = _bases.Count > 0
            ? $" : {string.Join(", ", _bases)}"
            : string.Empty;

        string parametersClause = _parameters.Count > 0
            ? $"({string.Join(", ", _parameters)})"
            : string.Empty;

        code.WriteLine($"{_containerType} {_name}{parametersClause}{basesClause}");

        var bodyContent = CodeBlock.FromBlocks(_contents);

        code.WriteLine("{");
        code.WriteLine($"    {bodyContent.Indent()}");
        code.WriteLine("}");

        return code;
    }
}
