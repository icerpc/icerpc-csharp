// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.ProtocGen;

internal static class MessageDescriptorExtensions
{
    internal static string GetFullyQualifiedType(this MessageDescriptor messageDescriptor) =>
        $"{messageDescriptor.File.GetCsharpNamespace()}.{messageDescriptor.Name}";

    internal static string GetType(this MessageDescriptor messageDescriptor, string scope)
    {
        string csharpNamespace = messageDescriptor.File.GetCsharpNamespace();
        if (scope == csharpNamespace)
        {
            return messageDescriptor.Name;
        }
        else
        {
            return $"{csharpNamespace}.{messageDescriptor.Name}";
        }
    }
}
