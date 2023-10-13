// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Represents a C# definition that is the parent of a service definition.</summary>
internal class ContainerDefinition
{
    /// <summary>Gets the keyword definition (struct, class, or record).</summary>
    internal string Keyword { get; }

    /// <summary>Gets the definition name.</summary>
    internal string Name { get; }

    /// <summary>Gets or sets the parent definition in which this definition is nested, null if this definition is
    /// not nested into another definition.</summary>
    internal ContainerDefinition? Parent { get; set; }

    internal ContainerDefinition(string name, string keyword)
    {
        Name = name;
        Keyword = keyword;
    }
}
