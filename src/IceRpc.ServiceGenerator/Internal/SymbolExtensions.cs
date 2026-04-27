// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Extension methods for <see cref="ISymbol"/>.</summary>
internal static class SymbolExtensions
{
    internal static AttributeData? FindAttribute(this ISymbol symbol, INamedTypeSymbol attributeSymbol)
    {
        ImmutableArray<AttributeData> attributes = symbol.GetAttributes();
        foreach (AttributeData attribute in attributes)
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol))
            {
                return attribute;
            }
        }
        return null;
    }

    internal static string GetFullName(this ISymbol symbol)
    {
        if (symbol is INamespaceSymbol namespaceSymbol && namespaceSymbol.IsGlobalNamespace)
        {
            return "";
        }
        else
        {
            string containingSymbolName = symbol.ContainingSymbol.GetFullName();
            return containingSymbolName.Length == 0 ? symbol.Name : $"{containingSymbolName}.{symbol.Name}";
        }
    }

    /// <summary>Checks whether <paramref name="type"/> is the non-generic <c>System.Threading.Tasks.ValueTask</c> or
    /// the generic <c>System.Threading.Tasks.ValueTask&lt;T&gt;</c>.</summary>
    internal static bool IsValueTask(
        this INamedTypeSymbol type,
        INamedTypeSymbol? valueTaskSymbol,
        INamedTypeSymbol? genericValueTaskSymbol) =>
        SymbolEqualityComparer.Default.Equals(type, valueTaskSymbol) ||
        SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, genericValueTaskSymbol);
}
