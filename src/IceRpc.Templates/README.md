# Templates for IceRPC

[Source code][source] | [Package][package]

IceRpc.Templates provides `dotnet new` project templates for [IceRPC][icerpc]. The following templates are included:

| Template Name            | Description                                                                                                  |
|--------------------------|--------------------------------------------------------------------------------------------------------------|
| `icerpc-slice-client`    | A project template for creating an IceRPC + Slice client console application.                                |
| `icerpc-slice-server`    | A project template for creating an IceRPC + Slice server console application.                                |
| `icerpc-slice-di-client` | A project template for creating an IceRPC + Slice client console application using Microsoft's DI container. |
| `icerpc-slice-di-server` | A project template for creating an IceRPC + Slice server console application using Microsoft's DI container. |

## Installation

``` shell
dotnet new install IceRpc.Templates
```

## Sample Code

Create a command line server application:

``` shell
dotnet new icerpc-slice-server -o MyServer
cd MyServer
dotnet build
dotnet run
```

Create a command line client application:

``` shell
dotnet new icerpc-slice-client -o MyClient
cd MyClient
dotnet build
dotnet run
```

[icerpc]: https://www.nuget.org/packages/IceRpc
[package]: https://www.nuget.org/packages/IceRpc.Templates
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Templates
