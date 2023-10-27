// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.ProtocGen;

internal static class MessageDescriptorExtensions
{
    internal static string GetType(this MessageDescriptor messageDescriptor, string scope, bool streaming)
    {
        string csharpNamespace = messageDescriptor.File.GetCsharpNamespace();
        string csharpType = scope == csharpNamespace ?
            messageDescriptor.Name : $"global::{csharpNamespace}.{messageDescriptor.Name}";
        if (streaming)
        {
            csharpType = $"global::System.Collections.Generic.IAsyncEnumerable<{csharpType}>";
        }
        return csharpType;
    }

    internal static string GetParserType(this MessageDescriptor messageDescriptor, string scope)
    {
        string csharpNamespace = messageDescriptor.File.GetCsharpNamespace();
        string csharpType = scope == csharpNamespace ?
            messageDescriptor.Name : $"global::{csharpNamespace}.{messageDescriptor.Name}";
        return $"{csharpType}.Parser";
    }
}
