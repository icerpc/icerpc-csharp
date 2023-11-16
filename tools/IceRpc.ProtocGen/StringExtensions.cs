// Copyright (c) ZeroC, Inc.

namespace IceRpc.ProtocGen;

internal static class StringExtensions
{
    /// <summary>Removes consecutive empty lines, and empty lines that are between an open and a close brace '{', '}'
    /// or between consecutive close '}'.</summary>
    /// <param name="value">The string to be processed.</param>
    /// <returns>The processed string without consecutive empty lines, and without empty lines that are between an open
    /// and a close brace '{', '}' or between consecutive close '}'.</returns>
    internal static string RemoveSuperfluousEmptyLines(this string value)
    {
        string[] lines = value.Split('\n');
        var processedLines = new List<string>();
        string? previous = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string current = lines[i];
            string? next = (i + 1 < lines.Length) ? lines[i + 1].Trim() : null;

            if (ShouldKeepLine(previous, current, next))
            {
                processedLines.Add(current);
                previous = current.Trim();
            }
        }
        return string.Join("\n", processedLines);

        static bool ShouldKeepLine(string? previous, string current, string? next)
        {
            if (!string.IsNullOrWhiteSpace(current.Trim()))
            {
                return true;
            }
            else if (string.IsNullOrWhiteSpace(next))
            {
                return false;
            }
            else if (next == "}" && (previous == "{" || previous == "}"))
            {
                return false;
            }
            return true;
        }
    }
}
