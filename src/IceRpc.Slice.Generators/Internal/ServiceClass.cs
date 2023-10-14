// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Represents a C# class that has the <c>IceRpc.Slice.SliceServiceAttribute</c> attribute and for which this
/// generator implements the <c>IceRpc.IDispatcher</c> interface.</summary>
internal class ServiceClass : ContainerDefinition
{
    /// <summary>Gets a value indicating whether the service has a base service class.</summary>
    internal bool HasBaseServiceClass { get; }

    /// <summary>Gets the scope of this definition.</summary>
    internal string Scope { get; }

    /// <summary>Gets a value indicating whether the service is a sealed type.</summary>
    internal bool IsSealed { get; }

    /// <summary>Gets the service methods implemented by the service.</summary>
    /// <remarks>It doesn't include the service methods implemented by the base service definition if any.</remarks>
    internal IReadOnlyList<ServiceMethod> ServiceMethods { get; }

    internal ServiceClass(
        string name,
        string scope,
        string keyword,
        IReadOnlyList<ServiceMethod> serviceMethods,
        bool hasBaseServiceClass,
        bool isSealed)
        : base(name, keyword)
    {
        Scope = scope;
        ServiceMethods = serviceMethods;
        HasBaseServiceClass = hasBaseServiceClass;
        IsSealed = isSealed;
    }
}
