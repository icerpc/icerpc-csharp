# IceRPC for C# Examples

This folder contains example applications for IceRPC. Each example is a simple client-server application that
demonstrates a specific feature or programming technique.

## Branch and NuGet packages

The project files for the examples use NuGet packages for IceRPC and Slice. The version of the referenced NuGet packages
is branch-specific:

| icerpc-csharp branch | Referenced NuGet package version | .NET version       |
|----------------------|----------------------------------|--------------------|
| 0.1.x                | 0.1.*                            | .NET 7.0           |
| 0.2.x                | 0.2.*                            | .NET 8.0           |
| 0.3.x                | 0.3.*                            | .NET 8.0           |
| 0.4.x                | 0.4.*                            | .NET 8.0, .NET 9.0 |
| main                 | 0.5.0-nightly.*                  | .NET 10.0          |

If you want to build the examples for a released version (such as 0.4.x), please checkout the corresponding release
branch. For example:

```shell
git checkout -b 0.4.x origin/0.4.x
```

Then, when you build an example, the build uses the NuGet packages published on nuget.org.

The examples on the main branch are configured to use the IceRPC packages from the ZeroC nightly repository. If you
want to build the examples on main against a local build of IceRPC instead, you must first build and publish the NuGet
packages locally, as described in [BUILDING]. Then pass the desired version to the build command, for example:

```shell
dotnet build /p:IceRpcVersion=0.5.0-preview1
```

## Examples

|                         |                                                                                                          |
|-------------------------|----------------------------------------------------------------------------------------------------------|
| [json](./json/)         | Contains examples that use the IceRPC core APIs to send and receive JSON-encoded requests and responses. |
| [slice](./slice/)       | Contains examples that use the IceRPC + Slice integration.                                               |
| [protobuf](./protobuf/) | Contains examples that use the IceRPC + Protobuf integration.                                            |

[BUILDING]: ../BUILDING.md
