// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf.Generators.Internal;

internal static class StringExtensions
{
    internal static string WithIndent(this string contents, string indent) =>
        string.Join("\n", contents.Split('\n').Select(value => $"{indent}{value}")).Trim();

    internal static string TrimTrailingWhiteSpaces(this string contents) =>
        string.Join("\n", contents.Split('\n').Select(value => value.TrimEnd()));
}
