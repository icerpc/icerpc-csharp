# Slice Tools for IceRPC

IceRpc.Slice.Tools allows you to compile Slice source definitions (in `.slice` files) into C# code (in `.cs` files)
within MSBuild projects. The generated C# code depends on [IceRPC][icerpc].

This package includes the Slice compiler for C#, `slicec-cs`. This compiler is a native tool with binaries for
Linux (x64 and arm64), macOS (x64 and arm64) and Windows (x64).

Once you've added the IceRpc.Slice.Tools NuGet package to your project, the Slice files of your project are
automatically compiled into C# files every time you build this project.

> When `slicec-cs` compiles a Slice file and the resulting .cs file is identical to the existing output .cs file,
> the existing .cs file is kept as-is; the output file is overwritten only when something changed.

[Source code][source] | [Package][package] | [slicec-cs documentation][slicec-cs] | [Slice documentation][slice]

## Adding Slice files to your project

The `SliceFile` item type represents a Slice file that will be compiled into a C# file using the `slicec-cs` compiler. By
default, all `.slice` files located in the project's home directory and any of its subdirectories, recursively, are added
to the project as `SliceFile` items.

You can prevent this auto-inclusion of `.slice` files by setting either [`EnableDefaultItems`][default_items] or
`EnableDefaultSliceFileItems` to `false`. The default value of these properties is `true`.

You can also add files to your project's Slice files explicitly with the `SliceFile` item type.

For example:
``` xml
<ItemGroup>
    <SliceFile Include="../Greeter.slice"/>
</ItemGroup>
```

This adds `Greeter.slice` to your project's Slice files even though this file is not in the project's home
directory or any of its subdirectories.

> Slice files must have a `.slice` extension.

## SliceFile item metadata

You can use the following `SliceFile` item metadata to customize the compilation of your Slice files:

| Name              | Default   | Description                                                                                                                  |
|-------------------|-----------|------------------------------------------------------------------------------------------------------------------------------|
| AdditionalOptions |           | Additional options to pass to the `slicec-cs` compiler.                                                                      |
| OutputDir         | generated | Output directory for the generated code. This metadata corresponds to the `--output-dir` option of the `slicec-cs` compiler. |
| Pack              | `false`   | Whether or not to include the items in the NuGet package.                                                                    |
| PackagePath       | slice     | The target path in the NuGet package.                                                                                        |

## Adding Slice directories to the compiler's reference path

To add a directory to the compiler's reference path, use the `SliceDirectory` item type. These items represent
directories containing Slice files that the compiler references, but for which no code is generated. The full path of
these items is items is passed to the `-R` option of the slicec-cs compiler. This is typically used to reference Slice
definitions that are required but compiled in a separate project or package.

``` xml
<ItemGroup>
    <SliceDirectory Include="$(MSBuildThisFileDirectory)../slice"/>
</ItemGroup>
```


[default_items]: https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#enabledefaultitems
[icerpc]: https://www.nuget.org/packages/IceRpc
[package]: https://www.nuget.org/packages/IceRpc.Slice.Tools
[slice]: https://docs.testing.zeroc.com/docs/slice
[slicec-cs]: TODO
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/tools.IceRpc.Slice.Tools
