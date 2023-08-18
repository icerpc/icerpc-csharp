// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Represents a C# definition that has the IceRpc.Slice.SliceServiceAttribute and for which this generator
/// implements the <c>IceRpc.IDispatcher</c> interface.</summary>
internal class ServiceDefinition : ContainerDefinition
{
    /// <summary>Gets a value indicating whether the service has a base service definition.</summary>
    internal bool HasBaseServiceDefinition { get; }

    /// <summary>Gets the C# namespace containing this definition.</summary>
    internal string ContainingNamespace { get; }

    /// <summary>Gets a value indicating whether the service is a sealed type.</summary>
    internal bool IsSealed { get; }

    /// <summary>Gets the service type IDs.</summary>
    internal SortedSet<string> TypeIds { get; }

    /// <summary>Gets the service methods implemented by the service.</summary>
    /// <remarks>It doesn't include the service methods implemented by the base service definition if any.</remarks>
    internal IReadOnlyList<ServiceMethod> ServiceMethods { get; }

    internal ServiceDefinition(
        string name,
        string containingNamespace,
        string keyword,
        IReadOnlyList<ServiceMethod> serviceMethods,
        bool hasBaseServiceDefinition,
        bool isSealed,
        SortedSet<string> typeIds)
        : base(name, keyword)
    {
        ContainingNamespace = containingNamespace;
        ServiceMethods = serviceMethods;
        HasBaseServiceDefinition = hasBaseServiceDefinition;
        IsSealed = isSealed;
        TypeIds = typeIds;
    }
}
