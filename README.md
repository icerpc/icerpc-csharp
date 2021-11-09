# IceRPC

![.github/workflows/dotnet.yml](https://github.com/zeroc-ice/icerpc-csharp/workflows/.NET/badge.svg?branch=main)

## Building

The build depends on NuGet packages that are not publicly available, for accessing this packages you must create a
`nuget.config` file with the following contents.

You must replace:
* USERNAME with the name of your user account on GitHub
* TOKEN with your personal access token. Create your token from https://github.com/settings/tokens and give it the
  `read:packages` permission.

```
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <clear />
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

## Testing

The test suite can be run from the command line by running `dotnet test` command in the repository top-level
directory, this command builds `IceRpc.sln` solution an execute all tests from the solution.

For additional options see <https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test>.

You can also run the tests from Visual Studio and Visual Studio for Mac using the builtin test explorer, in this
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
reportgenerator "-reports:tests/*/TestResults/*/coverage.cobertura.xml" "-targetdir:tests/TestRerport"
```

For convenience you can do the same using "CodeCoverageReport" msbuild target:

```
dotnet msbuild build/build.proj /t:CodeCoverageReport
```

## Building Example Programs

You can build each demo by using `dotnet build` command and the corresponding solution or project files, the example
programs are configured to use IceRCP NuGet packages.

If you want to build all examples at once run:

```
dotnet msbuild build/build.proj /t:BuildExamples /restore
```

If you want to use the IceRPC distribution from this repository instead of IceRPC from a published NuGet package, you need
to build and install the NuGet package from this repository before building the examples, this can be done by running the
following command:

```
dotnet msbuild build/build.proj /t:InstallLocalPackages /restore
```
