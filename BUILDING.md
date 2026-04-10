# Building IceRPC from source <!-- omit in toc -->

- [Prerequisites](#prerequisites)
- [Building IceRPC](#building-icerpc)
- [Running the tests](#running-the-tests)
- [Generating the code coverage reports](#generating-the-code-coverage-reports)
- [Generating the API reference](#generating-the-api-reference)

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
