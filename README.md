# IceRPC

[![Continuous Integration](https://github.com/icerpc/icerpc-csharp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/icerpc/icerpc-csharp/actions/workflows/dotnet.yml)

- [IceRPC](#icerpc)
- [Build Requirements](#build-requirements)
- [Building](#building)
- [Testing](#testing)
- [Examples](#examples)
- [Project Templates](#project-templates)
- [Packaging](#packaging)
- [Generating API Documentation](#generating-api-documentation)

## Build Requirements

Building IceRpc requires Rust and .NET development environments:

- A Rust development environment.
- The .NET 7.0 SDK.

The build system for the `slicec-cs` compiler fetches the `slicec` library from the `slicec` private repository. If the
build fails to fetch `slicec` with a permission denied error, set the following environment variable:

```shell
export CARGO_NET_GIT_FETCH_WITH_CLI=true
```

and then try again. This tells Cargo to use git's executable to fetch dependencies instead of its own.

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

## Examples

The [examples](./examples) directory contains example applications to help you get started using IceRPC.

## Project Templates

The `IceRpc.ProjectTemplates` NuGet package provides project templates for `dotnet new`. You can install the templates
using the following command:

```shell
dotnet new install IceRpc.ProjectTemplates
```

You can install the templates from this source distribution instead of the ones from the published NuGet packages using
the following command:

Linux or macOS

```shell
./build.sh install-templates
```

Windows

```shell
build.cmd install-templates
```

The `IceRpc.ProjectTemplates` package provides the following templates:

| Template Name      | Description                                    |
| ------------------ | ---------------------------------------------- |
| `icerpc-client` | Template for command line client applications. |
| `icerpc-server` | Template for command line server applications. |

### Usage

```shell
dotnet new <template-name>
```

> :point_up: `dotnet new -h` for help.

For additional options see https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new

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

## Generating API Documentation

Before generating reference documentation for the IceRPC API, ensure that you have the [docfx](1) command in your
system's PATH, with version 2.63 or higher.

To generate the documentation, use the following command:

Linux or macOS

```shell
./build.sh doc
```

Windows

```shell
build.cmd doc
```

The resulting documentation will be located in the `doc\_site` directory.

[1]: https://www.nuget.org/packages/docfx
