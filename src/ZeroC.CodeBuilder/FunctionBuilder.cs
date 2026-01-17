// Copyright (c) ZeroC, Inc.

namespace ZeroC.CodeBuilder;

/// <summary>A builder for creating C# functions (methods, constructors).</summary>
public sealed class FunctionBuilder : IBuilder, IAttributeBuilder<FunctionBuilder>, ICommentBuilder<FunctionBuilder>
{
    private readonly string _access;
    private readonly List<string> _attributes = [];
    private readonly List<string> _baseArguments = [];
    private readonly List<CommentTag> _comments = [];
    private readonly FunctionType _functionType;
    private readonly string _name;
    private readonly List<string> _parameters = [];
    private readonly string _returnType;
    private CodeBlock _body = new();
    private bool _inheritDoc;

    /// <summary>Initializes a new instance of the <see cref="FunctionBuilder"/> class.</summary>
    /// <param name="access">The access modifier (e.g., "public", "private").</param>
    /// <param name="returnType">The return type of the function.</param>
    /// <param name="name">The name of the function.</param>
    /// <param name="functionType">The type of function body.</param>
    public FunctionBuilder(string access, string returnType, string name, FunctionType functionType)
    {
        _access = access;
        _returnType = returnType;
        _name = name;
        _functionType = functionType;
    }

    /// <inheritdoc/>
    public FunctionBuilder AddAttribute(string attribute)
    {
        _attributes.Add(attribute);
        return this;
    }

    /// <summary>Adds arguments to pass to the base constructor.</summary>
    /// <param name="arguments">The arguments to pass to base.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionBuilder AddBaseParameters(IEnumerable<string> arguments)
    {
        _baseArguments.AddRange(arguments);
        return this;
    }

    /// <inheritdoc/>
    public FunctionBuilder AddComment(string tag, string content)
    {
        _comments.Add(new CommentTag(tag, content));
        return this;
    }

    /// <inheritdoc/>
    public FunctionBuilder AddComment(string tag, string attributeName, string attributeValue, string content)
    {
        _comments.Add(new CommentTag(tag, attributeName, attributeValue, content));
        return this;
    }

    /// <inheritdoc/>
    public FunctionBuilder AddComments(IEnumerable<CommentTag> comments)
    {
        _comments.AddRange(comments);
        return this;
    }

    /// <summary>Adds a parameter to the function.</summary>
    /// <param name="paramType">The parameter type.</param>
    /// <param name="paramName">The parameter name.</param>
    /// <param name="defaultValue">An optional default value.</param>
    /// <param name="docComment">An optional documentation comment.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionBuilder AddParameter(
        string paramType,
        string paramName,
        string? defaultValue = null,
        string? docComment = null)
    {
        string defaultPart = defaultValue is not null ? $" = {defaultValue}" : string.Empty;
        _parameters.Add($"{paramType} {paramName}{defaultPart}");

        if (docComment is not null)
        {
            AddComment("param", "name", paramName, docComment);
        }

        return this;
    }

    /// <summary>Adds the SetsRequiredMembers attribute.</summary>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionBuilder AddSetsRequiredMembersAttribute() =>
        AddAttribute("global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers");

    /// <inheritdoc/>
    public CodeBlock Build()
    {
        var code = new CodeBlock();

        if (_inheritDoc)
        {
            code.WriteLine("/// <inheritdoc/>");
        }
        else
        {
            foreach (var comment in _comments)
            {
                code.WriteLine(comment.ToString());
            }
        }

        foreach (var attribute in _attributes)
        {
            code.WriteLine($"[{attribute}]");
        }

        // Build the signature
        code.Write($"{_access} ");
        code.Write($"{_returnType} ");

        if (_parameters.Count > 1)
        {
            code.Write($"{_name}(\n    {string.Join($",\n    ", _parameters)})");
        }
        else
        {
            code.Write($"{_name}({string.Join(", ", _parameters)})");
        }

        // Add base constructor call if present
        if (_baseArguments.Count > 0)
        {
            code.Write($"\n    : base({string.Join(", ", _baseArguments)})");
        }

        // Add the body based on function type
        switch (_functionType)
        {
            case FunctionType.Declaration:
                code.WriteLine(";");
                break;

            case FunctionType.ExpressionBody:
                if (_body.IsEmpty)
                {
                    code.WriteLine(" => { };");
                }
                else
                {
                    code.WriteLine(@$" =>
        {_body.Indent()};");
                }
                break;

            case FunctionType.BlockBody:
                code.WriteLine("\n{");
                code.WriteLine($"    {_body.Indent()}");
                code.WriteLine("}");
                break;
        }

        return code;
    }

    /// <summary>Sets the body of the function.</summary>
    /// <param name="body">The function body.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionBuilder SetBody(CodeBlock body)
    {
        _body = body;
        return this;
    }

    /// <summary>Sets whether to use inheritdoc instead of explicit comments.</summary>
    /// <param name="inheritDoc">True to use inheritdoc.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionBuilder SetInheritDoc(bool inheritDoc)
    {
        _inheritDoc = inheritDoc;
        return this;
    }
}
