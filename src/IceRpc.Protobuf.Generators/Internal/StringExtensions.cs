// Copyright (c) ZeroC, Inc.

using System.Runtime.InteropServices;

namespace IceRpc.Protobuf.Generators.Internal;

internal static class StringExtensions
{
    private static readonly string _newLine = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\r\n" : "\n";

    internal static string WithIndent(this string contents, string indent) =>
        string.Join("\n", contents.Split('\n').Select(value => $"{indent}{value}")).Trim();

    internal static string TrimTrailingWhiteSpaces(this string contents) =>
        string.Join("\n", contents.Split('\n').Select(value => value.TrimEnd()));

    // TODO Once generators can target .NET 8 we can use String.ReplaceLineEndings and remove this implementation.
    internal static string ReplaceLineEndings(this string contents)
    {
        if (contents.IndexOf("\r\n") != -1)
        {
            contents = contents.Replace("\r\n", "\n");
        }
        if (_newLine != "\n")
        {
            contents = contents.Replace("\n", _newLine);
        }
        return contents;
    }
}
