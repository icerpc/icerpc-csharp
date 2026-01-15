// Copyright (c) ZeroC, Inc.

namespace ZeroC.CodeBuilder;

/// <summary>A builder for creating function call expressions.</summary>
public sealed class FunctionCallBuilder : IBuilder
{
    private readonly string _callable;
    private readonly List<string> _arguments = new();
    private string? _typeArgument;
    private bool _argumentsOnNewline;
    private bool _useSemicolon = true;

    /// <summary>Initializes a new instance of the <see cref="FunctionCallBuilder"/> class.</summary>
    /// <param name="callable">The callable expression (method name or expression).</param>
    public FunctionCallBuilder(string callable)
    {
        _callable = callable;
    }

    /// <summary>Sets whether arguments should be placed on separate lines.</summary>
    /// <param name="argumentsOnNewline">True to place arguments on new lines.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionCallBuilder ArgumentsOnNewline(bool argumentsOnNewline)
    {
        _argumentsOnNewline = argumentsOnNewline;
        return this;
    }

    /// <summary>Sets whether to append a semicolon after the call.</summary>
    /// <param name="useSemicolon">True to append a semicolon.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionCallBuilder UseSemicolon(bool useSemicolon)
    {
        _useSemicolon = useSemicolon;
        return this;
    }

    /// <summary>Sets the type argument for a generic method call.</summary>
    /// <typeparam name="T">The type that can be converted to string.</typeparam>
    /// <param name="typeArgument">The type argument.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionCallBuilder SetTypeArgument<T>(T typeArgument)
    {
        _typeArgument = typeArgument?.ToString();
        return this;
    }

    /// <summary>Adds an argument to the function call.</summary>
    /// <typeparam name="T">The type that can be converted to string.</typeparam>
    /// <param name="argument">The argument to add.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionCallBuilder AddArgument<T>(T argument)
    {
        _arguments.Add(argument?.ToString() ?? string.Empty);
        return this;
    }

    /// <summary>Adds an argument to the function call if the condition is true.</summary>
    /// <typeparam name="T">The type that can be converted to string.</typeparam>
    /// <param name="condition">The condition to check.</param>
    /// <param name="argument">The argument to add if condition is true.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionCallBuilder AddArgumentIf<T>(bool condition, T argument)
    {
        if (condition)
        {
            AddArgument(argument);
        }
        return this;
    }

    /// <summary>Adds an argument to the function call if it is not null.</summary>
    /// <typeparam name="T">The type that can be converted to string.</typeparam>
    /// <param name="argument">The optional argument to add.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionCallBuilder AddArgumentIfPresent<T>(T? argument)
        where T : class
    {
        if (argument is not null)
        {
            AddArgument(argument);
        }
        return this;
    }

    /// <summary>Adds an argument to the function call if it has a value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="argument">The optional argument to add.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public FunctionCallBuilder AddArgumentIfPresent<T>(T? argument)
        where T : struct
    {
        if (argument.HasValue)
        {
            AddArgument(argument.Value);
        }
        return this;
    }

    /// <inheritdoc/>
    public CodeBlock Build()
    {
        string typeArg = _typeArgument is not null ? $"<{_typeArgument}>" : string.Empty;

        string functionCall;
        if (_argumentsOnNewline && _arguments.Count > 0)
        {
            functionCall =
                $"{_callable}{typeArg}({Environment.NewLine}    {string.Join($",{Environment.NewLine}    ", _arguments)})";
        }
        else
        {
            functionCall = $"{_callable}{typeArg}({string.Join(", ", _arguments)})";
        }

        if (_useSemicolon)
        {
            functionCall += ";";
        }

        return new CodeBlock(functionCall);
    }
}
