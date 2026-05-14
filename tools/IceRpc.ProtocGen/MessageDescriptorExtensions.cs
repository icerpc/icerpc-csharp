// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.ProtocGen;

internal static class MessageDescriptorExtensions
{
    internal static string GetType(this MessageDescriptor messageDescriptor, string scope, bool streaming)
    {
        string csharpType = messageDescriptor.GetQualifiedTypeName(scope);
        if (streaming)
        {
            csharpType = $"global::System.Collections.Generic.IAsyncEnumerable<{csharpType}>";
        }
        return csharpType;
    }

    internal static string GetParserType(this MessageDescriptor messageDescriptor, string scope) =>
        $"{messageDescriptor.GetQualifiedTypeName(scope)}.Parser";

    private static string GetQualifiedTypeName(this MessageDescriptor messageDescriptor, string scope)
    {
        string csharpNamespace = messageDescriptor.File.GetCsharpNamespace();
        return scope == csharpNamespace ?
            messageDescriptor.Name :
            csharpNamespace.Length == 0 ?
                $"global::{messageDescriptor.Name}" :
                $"global::{csharpNamespace}.{messageDescriptor.Name}";
    }
}
