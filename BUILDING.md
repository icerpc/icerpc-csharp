# Building from source

- [Build Requirements](#build-requirements)
- [Building](#building)
- [Testing](#testing)
- [Code coverage reports](#code-coverage-report)
- [Examples](#examples)
- [Project Templates](#project-templates)
- [Packaging](#packaging)
- [Pushing Packages](#pushing-packages)
- [Generating API Documentation](#generating-api-documentation)

This document describes how to build and use the source code in this repository.

## Build Requirements

Building requires Rust and .NET development environments:

- A Rust development environment, the recommended way to install Rust is [rustup](https://rustup.rs/).
- The [.NET 7.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/7.0).

## Building

You can build IceRPC from a regular command prompt, using the following command:

Linux or macOS

```shell
./build.sh
```

Windows

```shell
build.cmd
```

This builds the [slicec-cs](./tools/slicec-cs) compiler, the IceRPC runtime assemblies, and the IceRPC unit tests with
the default debug configuration.

Visual Studio Code users can use `Tasks: Run Build Task...` from the command palette.

## Testing

You can run the test suite from the command line using the `dotnet test` command in the repository's top-level directory.
This command executes all tests from `IceRpc.sln` solution.

For additional options see <https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test>.

## Code coverage reports

Before generating the code coverage report, ensure that you have the
[reportgenerator](https://github.com/danielpalme/ReportGenerator) command in your system's PATH.

You can create the code coverage report using the following command:

Linux or macOS

```shell.
./build.sh test --coverage
```

Windows

```shell
build.cmd test -coverage
```

## Examples

To build the examples using the IceRPC distribution from this repository instead of the one from the published NuGet
packages, you need to build and install the NuGet packages from this repository before building the examples. You can
do this by running the following command:

For Linux and macOS

```shell
./build.sh --examples --srcdist
```

For Windows

```shell
build.cmd -examples -srcdist
```

## Packaging

After building IceRPC, you can create the corresponding NuGet packages using the following command:

Linux or macOS

```shell
./build.sh pack
```

Windows

```shell
build.cmd pack
```

### Slice Tools

When the `SLICEC_CS_STAGING_PATH` MSBuild property is set, the NuGet package includes the `slicec-cs` compiler for all
supported platforms instead of including the `slice-cs` compiler from the current source build. To create the package
with all supported compilers, you must ensure that the binaries for all the supported compilers are available in the
directory specified by `SLICEC_CS_STAGING_PATH` or the packaging task will fail with an error. The expected layout for
`SLICEC_CS_STAGING_PATH` is `<os-name>-<os-arch>/<compiler-executable>`.

The supported `<os-name>-<os-arch>` combinations are:

- `linux-x64`: Linux x86_64
- `linux-arm64`: Linux ARM64
- `macos-x64`: macOS x86_64
- `macos-arm64`: macOS Apple silicon
- `windows-x64`: Windows x64

## Push Packages

You can then push the packages to the `global-packages` source using:

Linux or macOS

```shell
./build.sh push
```

Windows

```shell
build.cmd push
```

This allows you to use the NuGet packages from the local `global-packages` source.

## Project Templates

You can install the `IceRpc.ProjectTemplates` templates from this source distribution instead of the ones from the
published NuGet packages using the following command:

Linux or macOS

```shell
./build.sh install-templates
```

Windows

```shell
build.cmd install-templates
```

## Generating API Documentation

Before generating reference documentation for the IceRPC API, ensure that you have the
[docfx](https://www.nuget.org/packages/docfx) command in your system's PATH, with version 2.63 or higher.

To generate the documentation, use the following command:

Linux or macOS

```shell
./build.sh doc
```

Windows

```shell
build.cmd doc
```

The resulting documentation will be located in the `docfx\_site` directory.




