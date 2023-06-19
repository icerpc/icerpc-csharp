# Templates for IceRPC

IceRpc.Templates provides `dotnet new` project templates for [IceRPC][icerpc]. The following templates are included:

| Template Name      | Description                                                                                          |
|--------------------|------------------------------------------------------------------------------------------------------|
| `icerpc-client`    | A project template for creating an IceRPC client console application.                                |
| `icerpc-server`    | A project template for creating an IceRPC server console application.                                |
| `icerpc-di-client` | A project template for creating an IceRPC client console application using Microsoft's DI container. |
| `icerpc-di-server` | A project template for creating an IceRPC server console application using Microsoft's DI container. |

## Installation

``` shell
dotnet new install IceRpc.Templates
```

## Sample Code

Create a command line server application:

``` shell
dotnet new icerpc-server -o MyServer
cd MyServer
dotnet build
dotnet run
```

Create a command line client application:

``` shell
dotnet new icerpc-client -o MyClient
cd MyClient
dotnet build
dotnet run
```

[Source code][source] | [Package][package]

[icerpc]: https://www.nuget.org/packages/IceRpc
[package]: https://www.nuget.org/packages/IceRpc.Templates
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Templates
