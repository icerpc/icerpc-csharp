This example application illustrates how IceRPC can interact with ZeroC Ice using the `ice` protocol and `Slice1` encoding.
The server application uses ZeroC Ice and the client application uses IceRPC.

Before building the `Server` project ensure that the slice2cs compiler is installed, for Windows the slice2cs compiler
is included with the `zeroc.ice.net` NuGet package used by the Server project and no additional steps are necessary,
for macOS and Linux see:

- (Building Ice Applications for .NET with the .NET Core SDK)[https://doc.zeroc.com/ice/3.7/release-notes/building-ice-applications-for-net#id-.BuildingIceApplicationsfor.NETv3.7-BuildingIceApplicationsfor.NETwiththe.NETCoreSDK]

For build instructions check the top-level [README.md](../../.../README.md).

First start the Server program:

```
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client program:

```
dotnet run --project Client/Client.csproj
```
