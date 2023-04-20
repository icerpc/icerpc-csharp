# IceRPC Tools

IceRPC tools provides support for compiling Slice source files (`.slice` files) in C# MSBuild projects.

This package contains:

- The `slicec-cs` compiler, for compiling Slice files to C#.
- The Slice definitions for IceRPC well-known types, IceRPC common definitions, and Ice interoperability.
- The MSBuild project integration.

## Contents

* [Usage](#usage)
* [Adding Slice Files to your Project](#adding-slice-files-to-your-project)
* [SliceC Item Metadata](#slicec-item-metadata)

## Usage

To use the IceRPC Slice Tools, add the `IceRpc.Slice.Tools` [NuGet package][1] to your C# project.

```
<ItemGroup>
  <PackageReference Include="IceRpc.Slice.Tools" Version="0.1.0" PrivateAssets="All" />
</ItemGroup>
```

## Adding Slice Files to your Project

You need to tell the IceRPC Tools which Slice files (files with a `.slice` extension) to compile, by adding these files to your project.

You can add all Slice files found in your project's home directory and any of its sub-directories (and sub-sub directories, recursively) to your project by setting both `EnableDefaultItems` and `EnableDefaultSliceCItems` to true.

The default value for `EnableDefaultSliceCItems` and `EnableDefaultItems` is true.

As an alternative, you can add Slice files to your project using the `SliceC` item type, for example:
```
<ItemGroup>
    <SliceC Include="../Hello.slice"/>
</ItemGroup>
```

## SliceC Item Metadata

The following metadata are recognized on the `<SliceC>` items:

| Name                 | Default     | Description                                                                                                                                                            |
| -------------------- | ----------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AdditionalOptions    | (na)        | Additional options to pass to the `slicec-cs` compiler.                                                                                                                |
| OutputDir            | generated   | The output directory for the generated code; corresponds to the `--output-dir`option of the `slicec-cs` compiler.                                                      |
| ReferencedFiles      | (na)        | Specify additional directories containing Slice files or Slice files to resolve Slice definitions during compilation. Corresponds to `-R` `slicec-cs` compiler option. |

[1]: https://www.nuget.org/packages/IceRpc.Slice.Tools/
