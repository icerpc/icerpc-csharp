// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    internal static string GetEscapedName(this ISymbol symbol) =>
        SyntaxFacts.GetKeywordKind(symbol.Name) != SyntaxKind.None ||
        SyntaxFacts.GetContextualKeywordKind(symbol.Name) != SyntaxKind.None ?
            $"@{symbol.Name}" : symbol.Name;

    internal static string GetFullName(this ISymbol symbol)
    {
        if (symbol is INamespaceSymbol namespaceSymbol && namespaceSymbol.IsGlobalNamespace)
        {
            return "";
        }
        else
        {
            string containingSymbolName = symbol.ContainingSymbol.GetFullName();
            string name = symbol.GetEscapedName();
            return containingSymbolName.Length == 0 ? name : $"{containingSymbolName}.{name}";
        }
    }
}
