// Copyright (c) ZeroC, Inc.

using System.Runtime.InteropServices;

namespace IceRpc.ServiceGenerator.Internal;

internal static class StringExtensions
{
    private static readonly string _newLine = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\r\n" : "\n";

    // This is a polyfill for String.ReplaceLineEndings, which is not available with netstandard2.0.
    internal static string ReplaceLineEndings(this string contents)
    {
        if (contents.Contains("\r\n", StringComparison.Ordinal))
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
