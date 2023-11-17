// Copyright (c) ZeroC, Inc.

using System.Text;

namespace IceRpc.ProtocGen;

internal static class StringExtensions
{
    /// <summary>Removes consecutive empty lines, and empty lines that are before a close brace '}'.</summary>
    /// <param name="value">The string to be processed.</param>
    /// <returns>The processed string without consecutive empty lines, and empty lines that are before a close
    /// brace '}'.</returns>
    internal static string RemoveSuperfluousEmptyLines(this string value)
    {
        string[] lines = value.Split(Environment.NewLine, StringSplitOptions.None);
        var builder = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            string current = lines[i];
            string? next = (i + 1 < lines.Length) ? lines[i + 1].Trim() : null;

            if (ShouldKeepLine(current, next))
            {
                builder.AppendLine(current);
            }
        }
        return builder.ToString();

        static bool ShouldKeepLine(string current, string? next) =>
            !(string.IsNullOrWhiteSpace(current) && (string.IsNullOrWhiteSpace(next) || next == "}"));
    }
}
