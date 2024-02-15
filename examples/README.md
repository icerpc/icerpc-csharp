# IceRPC for C# Examples

This folder contains example applications for IceRPC. Each example is a simple client-server application that
demonstrates a specific feature or programming technique.

## Branch and NuGet packages

The project files for the examples use NuGet packages for IceRPC and Slice. The version of the referenced NuGet packages
is branch-specific:

| icerpc-csharp branch | Referenced NuGet package version | .NET version |
|----------------------|----------------------------------|--------------|
| 0.1.x                | 0.1.*                            | .NET 7.0     |
| 0.2.x                | 0.2.*                            | .NET 8.0     |
| 0.3.x                | 0.3.*                            | .NET 8.0     |
| main                 | Not yet published on nuget.org   | .NET 8.0     |

If you want to build the examples for a released version (such as 0.3.x), please checkout the corresponding release
branch. For example:

```shell
git checkout -b 0.3.x origin/0.3.x
```

Then, when you build an example, the build uses the NuGet packages published on nuget.org.

If you want to build the examples on the `main` branch, you first need to build and publish locally the latest version
of the NuGet packages, as described in [BUILDING].

## Examples

|                         |                                                                                                          |
|-------------------------|----------------------------------------------------------------------------------------------------------|
| [json](./json/)         | Contains examples that use the IceRPC core APIs to send and receive JSON-encoded requests and responses. |
| [slice](./slice/)       | Contains examples that use the IceRPC + Slice integration.                                               |
| [protobuf](./protobuf/) | Contains examples that use the IceRPC + Protobuf integration.                                            |

[BUILDING]: ../BUILDING.md
