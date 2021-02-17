# IceRPC

![.github/workflows/dotnet.yml](https://github.com/zeroc-ice/icerpc-csharp/workflows/.NET/badge.svg?branch=main)

# Testing

The test suite can be run from the command line by running `dotnet test` command in the repository top-level
directory, this command builds `IceRpc.sln` solution an execute all tests from the solution.

For additional options see https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test

You can also run the tests from Visual Studio and Visual Studio for Mac using the builtin test explorer, in this
case you need to use `IceRpc.sln` solution file.

Visual Studio Code users can install [.NET Core Test Explorer](https://marketplace.visualstudio.com/items?itemName=formulahendry.dotnet-test-explorer)
plug-in to run tests from it.


# Building Example Programs

You can build each demo by using `dotnet build` command and the corresponding solution or project files, the example
programs are configured to use IceRCP NuGet package.

If you want to use the IceRPC distribution from this repository instead of IceRPC from a published NuGet package, you need
to first build and install the NuGet package, this can be done by running the following command:

```
dotnet msbuild build/build.proj /t:InstallLocalPackages /restore
```


If you want to build all examples run:

```
dotnet msbuild build/build.proj /t:BuildExamples /restore
```

The above command first build IceRPC NuGet package, installs it locally and then build all solution files,
under examples directory.
