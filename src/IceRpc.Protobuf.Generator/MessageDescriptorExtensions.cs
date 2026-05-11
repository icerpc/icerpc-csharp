// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.Protobuf.Generator;

internal static class MessageDescriptorExtensions
{
    internal static string GetIncomingType(this MessageDescriptor messageDescriptor, string scope, bool streaming)
    {
        string csharpType = messageDescriptor.GetQualifiedTypeName(scope);
        return streaming ? $"global::IceRpc.IAsyncStream<{csharpType}>" : csharpType;
    }

    internal static string GetOutgoingType(this MessageDescriptor messageDescriptor, string scope, bool streaming)
    {
        string csharpType = messageDescriptor.GetQualifiedTypeName(scope);
        return streaming ? $"global::System.Collections.Generic.IAsyncEnumerable<{csharpType}>" : csharpType;
    }

    internal static string GetParserType(this MessageDescriptor messageDescriptor, string scope) =>
        $"{messageDescriptor.GetQualifiedTypeName(scope)}.Parser";

    private static string GetQualifiedTypeName(this MessageDescriptor messageDescriptor, string scope)
    {
        string csharpNamespace = messageDescriptor.File.GetCsharpNamespace();
        string qualifiedName = messageDescriptor.GetQualifiedCsharpName();
        return scope == csharpNamespace ?
            qualifiedName :
            csharpNamespace.Length == 0 ?
                $"global::{qualifiedName}" :
                $"global::{csharpNamespace}.{qualifiedName}";
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
