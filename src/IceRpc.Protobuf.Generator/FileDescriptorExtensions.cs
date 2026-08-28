// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;
using IceRpc.CaseConverter.Internal;

namespace IceRpc.Protobuf.Generator;

internal static class FileDescriptorExtensions
{
    internal static string GetCsharpNamespace(this FileDescriptor descriptor)
    {
        if (descriptor.GetOptions() is Google.Protobuf.Reflection.FileOptions fileOptions &&
            fileOptions.HasCsharpNamespace)
        {
            return fileOptions.CsharpNamespace;
        }
        else
        {
            return descriptor.Package.ToCsharpNamespace();
        }
    }
}
