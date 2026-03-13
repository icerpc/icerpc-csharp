// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using ZeroC.CodeBuilder;

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Represents an abstract method in a generated XxxService interface decorated with an IDL-specific attribute.
/// </summary>
internal abstract class ServiceMethod : IEquatable<ServiceMethod>
{
    /// <summary>Gets the name of the RPC operation, for example: "findObjectById".</summary>
    internal abstract string OperationName { get; }

    /// <summary>Gets the using directives required by the generated code.</summary>
    internal abstract IEnumerable<string> UsingDirectives { get; }

    public bool Equals(ServiceMethod? other)
    {
        if (other is null)
        {
            return false;
        }
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        return OperationName == other.OperationName;
    }

    public override bool Equals(object? obj) => Equals(obj as ServiceMethod);

    public override int GetHashCode() => OperationName.GetHashCode();

    /// <summary>Generates the dispatch case body for this method.</summary>
    /// <returns>The dispatch case body.</returns>
    internal abstract CodeBlock GenerateDispatchCaseBody();
}

/// <summary>Represents a factory for <see cref="ServiceMethod"/> instances.</summary>
internal interface IServiceMethodFactory
{
    /// <summary>Tries to create a service method from the specified method symbol.</summary>
    /// <param name="methodSymbol">The method symbol.</param>
    /// <param name="serviceMethod">When this method returns <see langword="true" />, contains the created
    /// service method.</param>
    /// <returns><see langword="true" /> if a service method was created; otherwise, <see langword="false" />.
    /// </returns>
    bool TryCreate(IMethodSymbol methodSymbol, out ServiceMethod? serviceMethod);
}

/// <summary>The common base implementation of <see cref="IServiceMethodFactory"/>.</summary>
internal abstract class ServiceMethodFactory : IServiceMethodFactory
{
    private readonly INamedTypeSymbol? _operationAttribute;

    public bool TryCreate(IMethodSymbol methodSymbol, out ServiceMethod? serviceMethod)
    {
        serviceMethod = null;
        if (_operationAttribute is null)
        {
            return false;
        }

        AttributeData? attribute = methodSymbol.FindAttribute(_operationAttribute);
        if (attribute is null)
        {
            return false;
        }

        foreach (TypedConstant typedConstant in attribute.ConstructorArguments)
        {
            if (typedConstant.Kind == TypedConstantKind.Error)
            {
                // If a compilation error was found, no need to keep evaluating other args.
                return false;
            }
        }

        serviceMethod = CreateServiceMethod(methodSymbol, attribute);
        return true;
    }

    private protected ServiceMethodFactory(INamedTypeSymbol? operationAttribute) =>
        _operationAttribute = operationAttribute;

    private protected abstract ServiceMethod CreateServiceMethod(
        IMethodSymbol methodSymbol,
        AttributeData attribute);
}
