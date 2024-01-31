# Protobuf Tools for IceRPC

IceRpc.Protobuf.Tools allows you to compile Protobuf definitions (in `.proto` files) into C# code (in `.cs` files)
within MSBuild projects.

This package includes the Protobuf compiler, `protoc`, and the `protoc-gen-icerpc-csharp` generator. The `protoc`
compiler is a native tool with binaries for Linux x64, macOS x64 and Windows x64. The `protoc-gen-icerpc-csharp`
generator is a .NET program and requires .NET 8 or later.

Once you've added the IceRpc.Protobuf.Tools NuGet package to your project, the Protobuf files of your project are
automatically compiled into C# files every time you build this project.

The `protoc` compiler contains several built-in generators, including the C# generator which is responsible for
generating the Protobuf serialization and deserialization code for C#. In addition to these built-in generators,
`protoc` also executes the external `protoc-gen-icerpc-csharp` generator which is responsible for generating the
IceRPC + Protobuf integration code.

The `protoc` C# generator generates a `.cs` file for each `.proto` file. The generated C# file has the same name
as the corresponding `.proto` file, but with the name converted to Pascal case. For example, `greeter.proto` is compiled
into `Greeter.cs`.

The `protoc-gen-icerpc-csharp` generator generates a `.IceRpc.cs` file for each `.proto` file. The generated C# file
has the same name as the corresponding `.proto` file, but with the name converted to Pascal case. For example,
`greeter.proto` is compiled into `Greeter.IceRpc.cs`.

[Source code][source] | [Package][package]

## Protobuf files and Protobuf search path

The Protobuf compiler accepts two main inputs:

- the Protobuf files to compile into C# code (the Protobuf files)
- directories that contain imported Protobuf files (the Protobuf search path)

You select which files to include in your project's Protobuf files with the `ProtoFile` item type. And you select which
paths to include in your project's Protobuf search path with the `ProtoSearchPath` item type.

By default, all `.proto` files located in your project's home directory and any of its subdirectories, recursively, are
included in `ProtoFile`. You can prevent this auto-inclusion of `.proto` files by setting either
[`EnableDefaultItems`][default-items] or `EnableDefaultProtoFileItems` to `false`. The default value of these properties
is `true`.

You can also add Protobuf files to your project explicitly. For example:

```xml
<ItemGroup>
    <ProtoFile Include="../greeter.proto"/>
</ItemGroup>
```

This adds `greeter.proto` to your project's Protobuf files even though this file is not in the project's home directory
or any of its subdirectories.

The Protobuf search path is an aggregate of the `ProtoSearchPath` defined in your project (if any) and the
`ProtoSearchPath` defined in NuGet packages referenced by your project.

For example, if your project's Protobuf files import files in directory `common/proto`, set your `ProtoSearchPath` as
follows:

```xml
<ItemGroup>
    <ProtoSearchPath Include="$(MSBuildThisFileDirectory)../common/proto"/>
</ItemGroup>
```

## ProtoFile item metadata

You can use the following `ProtoFile` item metadata to customize the compilation of your Proto files. Each
unique set of options results in a separate execution of `protoc`.

| Name              | Default   | Description                                                                                                                                      |
|-------------------|-----------|--------------------------------------------------------------------------------------------------------------------------------------------------|
| AdditionalOptions |           | Specifies additional options to pass to [`protoc`] compiler.                                                                                     |
| OutputDir         | generated | Sets the output directory for the generated code. This metadata corresponds to the `--csharp_out` and `--icerpc-csharp_out` options of `protoc`. |
| Pack              | `false`   | Specifies whether or not to include the items (Proto files) in the NuGet package.                                                                |
| PackagePath       | protobuf  | Sets the target path in the NuGet package. Used only when Pack is `true`.                                                                        |

## Generated code and NuGet packages

You need to reference the `IceRpc.Protobuf` NuGet package to compile the generated C# code. Referencing
`IceRpc.Protobuf` makes your project reference transitively [IceRpc][icerpc], [Google.Protobuf][google-protobuf] and
[System.IO.Pipelines][system-io-pipelines].

## Protobuf compiler

This package includes the `protoc` compiler binaries, and the Protobuf well-known type definitions from the
[Google.Protobuf.Tools][google-protobuf-tools] package.

Google.Protobuf.Tools does not include a `protoc` binary for macOS on Apple silicon. As a result, you need to install
[Rosetta 2] to build on an Apple silicon Mac computer.

[default-items]: https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#enabledefaultitems
[icerpc]: https://www.nuget.org/packages/IceRpc
[package]: https://www.nuget.org/packages/IceRpc.Protobuf.Tools
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/tools/IceRpc.Protobuf.Tools
[system-io-pipelines]: https://www.nuget.org/packages/System.IO.Pipelines
[google-protobuf]: https://www.nuget.org/packages/Google.Protobuf
[google-protobuf-tools]: https://www.nuget.org/packages/Google.Protobuf.Tools
[Rosetta 2]: https://en.wikipedia.org/wiki/Rosetta_(software)
