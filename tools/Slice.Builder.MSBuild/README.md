# Slice Builder for MSBuild

Slice Builder for MSBuild provides support for compiling Slice source files (`.slice` files) in C# MSBuild projects.

## Contents

* [Usage](#usage)
* [Adding Slice Files to your Project](#adding-slice-files-to-your-project)

## Usage

To use the Slice Builder for MSBuild, you just need to add the `Slice.Builder.MSBuild` [NuGet package][1] to your C#
project.

```
<ItemGroup>
  <PackageReference Include="Slice.Builder.MSBuild" Version="0.1.0" PrivateAssets="All" />
</ItemGroup>
```

## Adding Slice Files to your Project

You need to tell the Slice Builder for MSBuild which Slice files (files with a `.slice` extension) to compile, by
adding these files to your project.

You can add all Slice files found in your project's home directory and any of its sub-directories (and sub-sub
directories, recursively) to your project by setting both `EnableDefaultItems` and `EnableDefaultSliceCItems` to true.
The default value for `EnableDefaultSliceCItems` and `EnableDefaultItems` is true.

As an alternative, you can add Slice files to your project using the `SliceC` item type, for example:
```
<ItemGroup>
    <SliceC Include="../Hello.slice"/>
</ItemGroup>
```

[1]: https://www.nuget.org/packages/Slice.Builder.MSBuild/
