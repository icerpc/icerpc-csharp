// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Represents a C# class that has the IceRpc.Slice.SliceServiceAttribute and for which this generator
/// implements the <c>IceRpc.IDispatcher</c> interface.</summary>
internal class ServiceClass : ContainerDefinition
{
    /// <summary>Gets a value indicating whether the service has a base service class.</summary>
    internal bool HasBaseServiceClass { get; }

    /// <summary>Gets the C# namespace containing this definition.</summary>
    internal string ContainingNamespace { get; }

    /// <summary>Gets a value indicating whether the service must implment <c>Ice::Object</c> interface.</summary>
    internal bool ImplementIceObject { get; }

    /// <summary>Gets a value indicating whether the service is a sealed type.</summary>
    internal bool IsSealed { get; }

    /// <summary>Gets the service type IDs.</summary>
    internal SortedSet<string> TypeIds { get; }

    /// <summary>Gets the service methods implemented by the service.</summary>
    /// <remarks>It doesn't include the service methods implemented by the base service definition if any.</remarks>
    internal IReadOnlyList<ServiceMethod> ServiceMethods { get; }

    internal ServiceClass(
        string name,
        string containingNamespace,
        string keyword,
        IReadOnlyList<ServiceMethod> serviceMethods,
        bool hasBaseServiceClass,
        bool implementIceObject,
        bool isSealed,
        SortedSet<string> typeIds)
        : base(name, keyword)
    {
        ContainingNamespace = containingNamespace;
        ServiceMethods = serviceMethods;
        HasBaseServiceClass = hasBaseServiceClass;
        IsSealed = isSealed;
        ImplementIceObject = implementIceObject;
        TypeIds = typeIds;
    }
}
