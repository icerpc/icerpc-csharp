# Building IceRPC from source <!-- omit in toc -->

- [Prerequisites](#prerequisites)
- [Building IceRPC](#building-icerpc)
- [Running the tests](#running-the-tests)
- [Generating the code coverage reports](#generating-the-code-coverage-reports)
- [Generating the API reference](#generating-the-api-reference)
- [Building examples against a local source build](#building-examples-against-a-local-source-build)
  - [1. Publish the packages locally](#1-publish-the-packages-locally)
  - [2. Build the example with the local package version](#2-build-the-example-with-the-local-package-version)

## Prerequisites

1. .NET SDK\
Download the .NET 10.0 SDK from [dotnet.microsoft.com](https://dotnet.microsoft.com/en-us/download/dotnet).

2. docfx (optional)\
Required only for generating the API reference. Install with:

   ```shell
   dotnet tool update -g docfx
   ```

3. ReportGenerator (optional)\
Required only for generating code coverage reports. Install with:

   ```shell
   dotnet tool install -g dotnet-reportgenerator-globaltool
   ```

## Building IceRPC

```shell
dotnet build
```

This command builds all the tools, sources and tests with the default configuration (debug).

## Running the tests

```shell
dotnet test
```

This command executes all tests in the `IceRpc.slnx` solution. See
[dotnet-test](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test) for additional options.

## Generating the code coverage reports

First, run the tests with coverage collection enabled:

```shell
dotnet test --collect:"XPlat Code Coverage"
```

Then, generate the report:

```shell
reportgenerator -reports:"tests/*/TestResults/*/coverage.cobertura.xml" -targetdir:tests/CodeCoverageReport
```

The coverage report will be available in the `tests/CodeCoverageReport` directory.

## Generating the API reference

```shell
cd docfx
docfx metadata --property Configuration=Debug
docfx build
```

This generates the API reference into the `docfx/_site` directory. Start a local web server to view it:

```shell
docfx serve _site
```

## Building examples against a local source build

By default, the examples restore published IceRPC packages:

- On the `main` branch, from ZeroC's nightly repository.
- On other branches, from NuGet.org.

To build an example against packages produced from your local build, follow these steps.

### 1. Publish the packages locally

From the repository root, publish the IceRPC packages to your local NuGet global-packages folder.

On macOS and Linux:

```shell
./build/publish-local-packages.sh
```

On Windows:

```shell
.\build\publish-local-packages.ps1
```

### 2. Build the example with the local package version

From the example directory, run `dotnet build` and set `IceRpcVersion` to the version in
`build/IceRpc.Version.props`:

```shell
dotnet build /p:IceRpcVersion=<local-version>
```

For example, if `build/IceRpc.Version.props` contains `0.6.0-preview.1`:

```shell
dotnet build /p:IceRpcVersion=0.6.0-preview.1
```
