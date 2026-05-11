// Copyright (c) ZeroC, Inc.

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Represents a C# definition in which a C# service definition is nested.</summary>
internal class ContainerDefinition
{
    /// <summary>Gets the keyword of this definition (struct, class, or record).</summary>
    internal string Keyword { get; }

    /// <summary>Gets the name of this definition, including the type-parameter list when generic
    /// (e.g. <c>Foo&lt;T&gt;</c>).</summary>
    internal string Name { get; }

    /// <summary>Gets the number of type parameters, or 0 when this definition is not generic.</summary>
    internal int TypeParameterCount { get; }

    /// <summary>Gets the full name of this definition (does not include namespace).</summary>
    internal string FullName => Enclosing is null ? Name : $"{Enclosing.FullName}.{Name}";

    /// <summary>Gets a file-name-safe form of <see cref="FullName"/>, using the metadata-style arity suffix
    /// (e.g. <c>Outer.Foo`1</c>) instead of the angle-bracket type-parameter list.</summary>
    internal string FullFileName => Enclosing is null ? FileName : $"{Enclosing.FullFileName}.{FileName}";

    /// <summary>Gets or sets the enclosing definition in which this definition is nested.</summary>
    /// <value>The enclosing definition or <c>null</c> if this definition is not nested into another definition.</value>
    internal ContainerDefinition? Enclosing { get; set; }

    internal ContainerDefinition(string name, string keyword, int typeParameterCount = 0)
    {
        Name = name;
        Keyword = keyword;
        TypeParameterCount = typeParameterCount;
    }

    private string FileName
    {
        get
        {
            int angleBracket = Name.IndexOf('<');
            string identifier = angleBracket < 0 ? Name : Name.Substring(0, angleBracket);
            return TypeParameterCount > 0 ? $"{identifier}`{TypeParameterCount}" : identifier;
        }
    }
}
