# IceRPC

[![Continuous Integration](https://github.com/zeroc-ice/icerpc-csharp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/zeroc-ice/icerpc-csharp/actions/workflows/dotnet.yml)

- [IceRPC](#icerpc)
- [Build Requirements](#build-requirements)
- [Building](#building)
- [Testing](#testing)
- [Building Example Programs](#building-example-programs)
- [Project Templates](#project-templates)
- [Generating API Documentation](#generating-api-documentation)

## Build Requirements

Building IceRpc requires Rust and .NET development environments:

- A Rust development environment
- The .NET 7.0 SDK

The Slice compiler depends on `slicec` library, which is installed from`zeroc-ice/slicec` private repository, if the
Slice compiler build fails with a "Permission denied" error try setting the following environment variable:

```shell
export CARGO_NET_GIT_FETCH_WITH_CLI=true
```

This tells Cargo to use git's executable to fetch dependencies instead of it's own.

## Building

IceRpc can be built from a regular command prompt, using the following command

For Linux and macOS

```shell
./build.sh
```

For Windows

```shell
build.cmd
```

This builds the [slicec-cs](./tools/slicec-cs) compiler, the IceRpc runtime assemblies, and the IceRpc tests in the
default debug configuration.

Additionally, a build task is provided for building IceRpc within Visual Studio Code. This task has been configured
as the default build task, so you can invoke it by selecting `Tasks: Run Build Task...` from the command palette.

## Testing

The test suite can be run from the command line by running `dotnet test` command in the repository top-level
directory, this command builds `IceRpc.sln` solution an executes all tests from the solution.

For additional options see <https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test>.

You can also run the tests from Visual Studio and Visual Studio for Mac using the built-in test explorer, in this
case you need to use `IceRpc.sln` solution file.

Visual Studio Code users can install [.NET Core Test Explorer](https://marketplace.visualstudio.com/items?itemName=formulahendry.dotnet-test-explorer)
plug-in to run tests from it.

Code coverage reports can be generated using [ReportGenerator](https://github.com/danielpalme/ReportGenerator)

Install the reportgenerator dotnet tool

```shell
dotnet tool install -g dotnet-reportgenerator-globaltool
```

Run the tests with code coverage

```shell
dotnet test --collect:"XPlat Code Coverage"
```

Generate the test report

```shell
reportgenerator "-reports:tests/*/TestResults/*/coverage.cobertura.xml" "-targetdir:tests/TestReport"
```

You can do the same with the `--coverage` argument of the build script.

## Building Example Programs

You can build each demo by using `dotnet build` command and the corresponding solution or project files, the example
programs are configured to use IceRPC NuGet packages.

If you want to build all examples at once run the following command:

For Linux and macOS

```shell
./build.sh --examples
```

For Windows

```shell
build.cmd --examples
```

If you want to use the IceRPC distribution from this repository instead of IceRPC from a published NuGet package, you
will need to build and install the NuGet package from this repository before building the examples. This can be done by
running the following command:

For Linux and macOS

```shell
./build.sh --examples --srcdist
```

For Windows

```shell
build.cmd --examples --srcdist
```

## Project Templates

The `IceRpc.ProjectTemplates` NuGet packages provides project templates for `dotnet new`, install the templates using:

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
build.cmd install-templates
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
build.cmd doc
```

Upon completion the documentation is placed in the `doc\_site` directory.
