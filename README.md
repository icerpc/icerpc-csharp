# IceRPC

[![Continuous Integration](https://github.com/zeroc-ice/icerpc-csharp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/zeroc-ice/icerpc-csharp/actions/workflows/dotnet.yml)

- [IceRPC](#icerpc)
- [Build Requirements](#build-requirements)
- [Building](#building)
- [Packaging](#packaging)
- [Testing](#testing)
- [Examples](#examples)
- [Project Templates](#project-templates)
- [Generating API Documentation](#generating-api-documentation)

## Build Requirements

Building IceRpc requires Rust and .NET development environments:

- A Rust development environment
- The .NET 7.0 SDK

The build system for the `slicec-cs` compiler fetches the `slicec` library from the `slicec` private repository. If the
build fails to fetch `slicec` with a permission denied error, set the following environment variable:

```shell
export CARGO_NET_GIT_FETCH_WITH_CLI=true
```

and then try again. This tells Cargo to use git's executable to fetch dependencies instead of it's own.

## Building

You can build IceRPC from a regular command prompt, using the following commands

For Linux and macOS

```shell
./build.sh
```

For Windows

```shell
build
```

This builds the [slicec-cs](./tools/slicec-cs) compiler, the IceRPC runtime assemblies, and the IceRPC unit tests in the
default debug configuration.

Visual Studio Code users can use `Tasks: Run Build Task...` from the command palette.

## Packaging

After building IceRPC you can create the corresponding NuGet packages by running the following command

For Linux and macOS

```shell
./build.sh pack
```

For Windows

```shell
build pack
```

You can push the packages to the `global-packages` source with the following command:

For Linux and macOS

```shell
./build.sh push
```

For Windows

```shell
build push
```

This allow using the NuGet packages from the local `global-packages` source.

## Testing

You can run the test suite from the command line using the `dotnet test` command in the repository's top-level directory. This command builds `IceRpc.sln` solution an executes all tests from the solution.

For additional options see <https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test>.

To create a code coverage reports using [ReportGenerator](https://github.com/danielpalme/ReportGenerator)

Install the reportgenerator dotnet tool

```shell
dotnet tool install -g dotnet-reportgenerator-globaltool
```

Run the tests with code coverage

For Linux and macOS

```shell
./build.sh test --coverage
```

For Windows

```shell
build test --coverage
```

## Example Programs

The IceRPC example programs can be found in the [examples](./examples) directory of this repository.

## Project Templates

The `IceRpc.ProjectTemplates` NuGet package provides project templates for `dotnet new`, install the templates using:

```shell
dotnet new install IceRpc.ProjectTemplates
```

To install the templates using a source build run the following command:

For Linux and macOS

```shell
./build.sh install-templates
```

For Windows

```shell
build install-templates
```

The `IceRpc.ProjectTemplates` package provides the following templates:

| Template Name   | Description                                    |
| --------------- | ---------------------------------------------- |
| `icerpc-client` | Template for command line client applications. |
| `icerpc-server` | Template for command line server applications. |

### Usage

```shell
dotnet new <template-name>
```

> :point_up: `dotnet new -h` for help.

For additional options see https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new

## Generating API Documentation

Before generating the documentation ensure that the `docfx` command is present in your PATH; version 2.63 or greater is
required.

The reference documentation for IceRPC API can be built using the following command:

For Linux and macOS

```shell
./build.sh doc
```

For Windows

```shell
build doc
```

Upon completion the documentation is placed in the `doc\_site` directory.
