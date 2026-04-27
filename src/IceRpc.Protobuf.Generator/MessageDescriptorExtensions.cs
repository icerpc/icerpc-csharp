// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.Protobuf.Generator;

internal static class MessageDescriptorExtensions
{
    internal static string GetType(this MessageDescriptor messageDescriptor, string scope, bool streaming)
    {
        string csharpNamespace = messageDescriptor.File.GetCsharpNamespace();
        string qualifiedName = messageDescriptor.GetQualifiedCsharpName();
        string csharpType = scope == csharpNamespace ?
            qualifiedName : $"global::{csharpNamespace}.{qualifiedName}";
        if (streaming)
        {
            csharpType = $"global::System.Collections.Generic.IAsyncEnumerable<{csharpType}>";
        }
        return csharpType;
    }

    internal static string GetParserType(this MessageDescriptor messageDescriptor, string scope)
    {
        string csharpNamespace = messageDescriptor.File.GetCsharpNamespace();
        string qualifiedName = messageDescriptor.GetQualifiedCsharpName();
        string csharpType = scope == csharpNamespace ?
            qualifiedName : $"global::{csharpNamespace}.{qualifiedName}";
        return $"{csharpType}.Parser";
    }

    // Google's C# Protobuf generator emits nested message types inside a "Types" container class on each enclosing
    // message (e.g. message Outer { message Inner {} } becomes Outer.Types.Inner).
    private static string GetQualifiedCsharpName(this MessageDescriptor messageDescriptor)
    {
        string name = messageDescriptor.Name;
        for (MessageDescriptor? parent = messageDescriptor.ContainingType; parent is not null; parent = parent.ContainingType)
        {
            name = $"{parent.Name}.Types.{name}";
        }
        return name;
    }
}
