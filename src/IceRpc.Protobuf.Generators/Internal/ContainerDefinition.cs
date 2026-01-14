// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf.Generators.Internal;

/// <summary>Represents a C# definition in which a C# service definition is nested.</summary>
internal class ContainerDefinition
{
    /// <summary>Gets the keyword of this definition (struct, class, or record).</summary>
    internal string Keyword { get; }

    /// <summary>Gets the name of this definition.</summary>
    internal string Name { get; }

    /// <summary>Gets the full name of this definition (does not include namespace).</summary>
    internal string FullName => Enclosing is null ? Name : $"{Enclosing.FullName}.{Name}";

    /// <summary>Gets or sets the enclosing definition in which this definition is nested.</summary>
    /// <value>The enclosing definition or <c>null</c> if this definition is not nested into another definition.</value>
    internal ContainerDefinition? Enclosing { get; set; }

    internal ContainerDefinition(string name, string keyword)
    {
        Name = name;
        Keyword = keyword;
    }
}
