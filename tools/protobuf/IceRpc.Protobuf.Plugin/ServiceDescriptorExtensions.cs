// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.Protoc;

internal static class ServiceDescriptorExtensions
{
    internal static string GetFullyQualifiedType(this ServiceDescriptor serviceDescriptor) =>
        $"{serviceDescriptor.File.GetCsharpNamespace()}.{serviceDescriptor.Name}";
}
