# Building from source

- [Build Requirements](#build-requirements)
- [Building](#building)
- [Testing](#testing)
- [Code coverage reports](#code-coverage-report)
- [Examples](#examples)
- [Project Templates](#project-templates)
- [Packaging](#packaging)
- [Publishing Packages](#publishing-packages)
- [Generating the IceRpc API Reference](#generating-the-icerpc-api-reference)

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

> The build system for the slicec-cs compiler fetches the slicec library from the slicec repository. If the build fails
> to fetch slicec with a permission denied error, set the following environment variable:

``` shell.
export CARGO_NET_GIT_FETCH_WITH_CLI=true
```

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
./build.sh --test --coverage
```

Windows

```shell
build.cmd -test -coverage
```

## Examples

To use the IceRPC distribution from this repository instead of the one from the published NuGet packages, you'll need to
build and install the NuGet packages from this repository before building the examples. To do so, run the following
command:

For Linux and macOS

```shell
./build.sh --build --pack --publish --examples
```

For Windows

```shell
build.cmd -build -pack -publish -examples
```

## Packaging

You can create the IceRPC NuGet packages using the following command:

Linux or macOS

```shell
./build.sh --build --pack
```

Windows

```shell
build.cmd -build -pack
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

You can then publish the packages to the `global-packages` source using:

Linux or macOS

```shell
./build.sh --publish
```

Windows

```shell
build.cmd -publish
```

This allows you to use the NuGet packages from the local `global-packages` source.

## Project Templates

You can install the `IceRpc.ProjectTemplates` templates from this source distribution instead of the ones from the
published NuGet packages using the following command:

Linux or macOS

```shell
./build.sh --installTemplates
```

Windows

```shell
build.cmd -installTemplates
```

## Generating the IceRpc API Reference

We use [docfx](https://www.nuget.org/packages/docfx) to generate the IceRpc API reference.

Step 1: Install the latest version of docfx

```shell
dotnet tool update -g docfx
```

Step 2: Execute the build script

Linux or macOS
```shell
./build.sh --doc
```

Windows
```shell
build.cmd -doc
```

The build script generates the API reference into the `docfx\_site` directory. You can then start a local web server to
view this local API reference:

```shell
docfx serve docfx/_site
```
