// Copyright (c) ZeroC, Inc.

using System.Text;

namespace IceRpc.CaseConverter.Internal;

internal static class Converter
{
    internal static string ToPascalCase(this string value)
    {
        var sb = new StringBuilder(value.Length);
        char previousChar = '_';
        foreach (char c in value)
        {
            if (!char.IsLetterOrDigit(c))
            {
                previousChar = c;
                continue;
            }
            sb.Append(char.IsLetterOrDigit(previousChar) ? c : char.ToUpperInvariant(c));
            previousChar = c;
        }
        return sb.ToString();
    }
}
