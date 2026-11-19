# IceRPC for C# Examples

This folder contains example applications for IceRPC. Each example is a simple client-server application that
demonstrates a specific feature or programming technique.

## Branch and NuGet packages

The project files for the examples use NuGet packages for IceRPC and Slice. The version of the referenced NuGet packages
is branch-specific:

| icerpc-csharp branch | Referenced NuGet package version | .NET version       |
|----------------------|----------------------------------|--------------------|
| 0.6.x                | 0.6.*                            | .NET 10.0          |
| 0.7.x                | 0.7.*                            | .NET 11.0          |
| main                 | 0.8.0-nightly.*                  | .NET 11.0          |

If you want to build the examples for a released version (such as 0.7.x), please checkout the corresponding release
branch. For example:

```shell
git checkout -b 0.7.x origin/0.7.x
```

Then, when you build an example, the build uses the NuGet packages published on nuget.org.

The examples on the main branch are configured to use the IceRPC packages from the ZeroC nightly repository. If you
want to build the examples on main against a local build of IceRPC instead, you must first build and publish the NuGet
packages locally, as described in [BUILDING]. Then pass the desired version to the build command, for example:

```shell
dotnet build /p:IceRpcVersion=0.8.0-preview.1
```

## Examples

|                         |                                                                                                          |
|-------------------------|----------------------------------------------------------------------------------------------------------|
| [slice](./slice/)       | Contains examples that use the IceRPC + Slice integration.                                               |
| [protobuf](./protobuf/) | Contains examples that use the IceRPC + Protobuf integration.                                            |
| [ice](./ice/)           | Contains examples that use the IceRPC + Ice integration (primarily for interop with [ZeroC Ice]).        |
| [json](./json/)         | Contains examples that use the IceRPC core APIs to send and receive JSON-encoded requests and responses. |

[BUILDING]: ../BUILDING.md
[ZeroC Ice]: https://github.com/zeroc-ice/ice
