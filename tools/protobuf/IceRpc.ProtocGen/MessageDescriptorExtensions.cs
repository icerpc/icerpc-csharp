// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.ProtocGen;

internal static class MessageDescriptorExtensions
{
    internal static string GetFullyQualifiedType(this MessageDescriptor messageDescriptor) =>
        $"{messageDescriptor.File.GetCsharpNamespace()}.{messageDescriptor.Name}";
}
