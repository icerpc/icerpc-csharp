// Copyright (c) ZeroC, Inc.

using System.Text;

namespace ZeroC.Slice.Generator;

/// <summary>String extension methods for converting Slice identifiers to C# casing conventions.</summary>
internal static class StringExtensions
{
    /// <summary>Converts a Slice identifier to PascalCase.</summary>
    internal static string ToPascalCase(this string identifier)
    {
        var sb = new StringBuilder(identifier.Length);
        bool newWord = true;
        foreach (char c in identifier)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(newWord ? char.ToUpperInvariant(c) : c);
                newWord = false;
            }
            else
            {
                newWord = true;
            }
        }
        return sb.ToString();
    }

    /// <summary>Converts a Slice identifier to camelCase.</summary>
    internal static string ToCamelCase(this string identifier)
    {
        string pascal = identifier.ToPascalCase();
        if (pascal.Length == 0)
        {
            return pascal;
        }
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }
}
