# The Slice compiler for C#

This directory contains the source code for the Slice compiler for C#, for build instruction see [building][building]

The Slice compiler for C#, `slicec-cs`, allows you to compile Slice definitions (in `.slice` files) into C# code (in
`.cs` files).

## Generated files

The compiler generates a C# file for each Slice file it compiles, the generated file has the same name as the source
Slice file but `.cs` extension.

The generated files are placed in the current working directory, a different location can be specified using the
`--output-dir` compiler option.

If the generated C# file exists, the compiler only overwrites it when its contents are different from the contenst of
the new version.

## Referenced files

The Slice compilation can reference files for which no code is generated, the compiler `-R` option can be used to
add files or directories containing Slice files that are required for the compilation.

## Options

For detailed documentation about the supported  options use the `--help` compiler option.

[building][../../BUILDING.md]
