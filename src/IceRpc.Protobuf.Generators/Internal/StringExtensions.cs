// Copyright (c) ZeroC, Inc.

using System.Runtime.InteropServices;

namespace IceRpc.Protobuf.Generators.Internal;

internal static class StringExtensions
{
    private static readonly string _newLine = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\r\n" : "\n";

    /// <summary>Extension methods for <see cref="string" />.</summary>
    /// <param name="contents">The string contents.</param>
    extension(string contents)
    {
        // TODO Once generators can target .NET 8 we can use String.ReplaceLineEndings and remove this implementation.
        internal string ReplaceLineEndings()
        {
            string result = contents;
            if (result.IndexOf("\r\n") != -1)
            {
                result = result.Replace("\r\n", "\n");
            }
            if (_newLine != "\n")
            {
                result = result.Replace("\n", _newLine);
            }
            return result;
        }

        internal string TrimTrailingWhiteSpaces() =>
            string.Join("\n", contents.Split('\n').Select(value => value.TrimEnd()));

        internal string WithIndent(string indent) =>
            string.Join("\n", contents.Split('\n').Select(value => $"{indent}{value}")).Trim();
    }
}
