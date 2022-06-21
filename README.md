# IceRPC

![.github/workflows/dotnet.yml](https://github.com/zeroc-ice/icerpc-csharp/workflows/.NET/badge.svg?branch=main)

- [Build Requirements](#build-requirements)
- [Building](#building)
- [Testing](#testing)
- [Building Example Programs](#building-example-programs)
- [Project Templates](#project-templates)

## Build Requirements

Building IceRpc requires Rust and .NET development environments:

 - A Rust development environment
 - The .NET 6.0 SDK

The build depends on `IceRpc.Builder.MSBuild` NuGet package that is not publicly available, for accessing this package you must create a
`nuget.config` file with the following contents:

```
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <add key="github" value="https://nuget.pkg.github.com/zeroc-ice/index.json" />
    </packageSources>
    <packageSourceCredentials>
        <github>
            <add key="Username" value="USERNAME" />
            <add key="ClearTextPassword" value="TOKEN" />
        </github>
    </packageSourceCredentials>
</configuration>
```

You can create the `nuget.config` in the source folder or any folder up to the drive root.

You must replace:

* USERNAME with the name of your user account on GitHub
* TOKEN with your personal access token. Create your token from https://github.com/settings/tokens and give it the
  `read:packages` permission.

Additionally, it may be necessary to set the following environment variable if you get a "Permission denied" error:
```
export CARGO_NET_GIT_FETCH_WITH_CLI=true
```
This tells Cargo to use git's executable to fetch dependencies instead of it's own.

## Building

IceRpc can be built from a regular command prompt, using the following command

For Linux and macOS

```
./build.sh
```

For Windows

```
build.cmd
```

This builds the [slicec-cs](./tools/slicec-cs) compiler, the IceRpc runtime assemblies, and the IceRpc tests in the
default debug configuration.

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

```
dotnet tool install -g dotnet-reportgenerator-globaltool
```

Run the tests with code coverage

```
dotnet test --collect:"XPlat Code Coverage"
```

Generate the test report

```
reportgenerator "-reports:tests/*/TestResults/*/coverage.cobertura.xml" "-targetdir:tests/TestReport"
```

You can do the same with the `--coverage` argument of the build script.

## Building Example Programs

You can build each demo by using `dotnet build` command and the corresponding solution or project files, the example
programs are configured to use IceRPC NuGet packages.

If you want to build all examples at once run:

```
./build.sh --examples
```

If you want to use the IceRPC distribution from this repository instead of IceRPC from a published NuGet package, you need
to build and install the NuGet package from this repository before building the examples, this can be done by running the
following command:

```
./build.sh --examples --srcdist
```
## Project Templates

The `IceRpc.ProjectTemplates` NuGet packages provides project templates for `dotnet new`, install the templates using:

```
dotnet new install IceRpc.ProjectTemplates
```

Or for a source build:

```
./build.sh install-templates
```

The `IceRpc.ProjectTemplates` package provides the following templates:

| Template Name   | Description                                    |
| --------------- | ---------------------------------------------- |
|`icerpc-client`  | Template for command line client applications. |
|`icerpc-server`  | Template for command line server applications. |

### Usage:

```
dotnet new <template-name>
```

> :point_up: `dotnet new -h` for help.
