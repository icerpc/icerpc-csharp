# Slice Tools for IceRPC

IceRpc.Slice.Tools allows you compile Slice source definitions (in `.slice` files) into C# code 
(in `.cs` files) within MSBuild projects. The generated C# code depends on [IceRPC][icerpc].

This package includes `slicec-cs`, the Slice compiler for C#. This compiler is a native tool written in Rust.

[Source code][source] | [Package][package] | [slicec-cs documentation][slicec-cs] |  [Slice documentation][slice]

## Adding Slice files to your project

Once you've added IceRpc.Slice.Tools to your project, all the Slice files in the project's home directory and any
of its sub-directories (and sub-sub directories, recursively) are automatically compiled into C# files every time
you build this project.

TODO: describe that we don't change the files when the new C# files are identical to the old ones. Or I believe
that's what is happening. Is this already documented anywhere??

You can disable this automatic Slice compilation by setting either `EnableDefaultItems` or `EnableDefaultSliceCItems`
to `false`.

You can also specify explicitly which Slice file to compile with the `SliceC` item type. For example:
```
<ItemGroup>
    <SliceC Include="../Greeter.slice"/>
</ItemGroup>
```

## SliceC item metadata

You can use the following `SliceC` item metadata to customize the compilation of your Slice files:

| Name                 | Default     | Description                                                                                                                                                       |
| -------------------- | ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AdditionalOptions    |          | Additional options to pass to the `slicec-cs` compiler.                                                                                                           |
| OutputDir            | generated   | Output directory for the generated code. This metadata corresponds to the `--output-dir` option of the `slicec-cs` compiler.                                        |
| ReferencedFiles      |           | Reference Slice files. The Slice files compiled by `slicec-cs` can reference types and other definitions from these files. This metadata corresponds to the `-R` option of the `slicec-cs` compiler. |

TODO: fix metadata name above. Maybe simply Reference? Should Reference have a default? How do you reference the well-known types? The Ice Slice files?

[icerpc]: https://www.nuget.org/packages/IceRpc
[package]: https://www.nuget.org/packages/IceRpc.Slice.Tools
[slice]: https://docs.testing.zeroc.com/docs/slice
[slicec-cs]: TODO
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/tools.IceRpc.Slice.Tools
