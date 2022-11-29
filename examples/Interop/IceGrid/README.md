# IceGrid

This example application illustrates how an IceRPC client can call services in a Ice server managed by IceGrid.
The server application uses ZeroC Ice, and the client application uses IceRPC.

For build instructions check the top-level [README.md](../../../README.md).

First, start an IceGrid registry by following the instructions provided in any of the following `simple` IceGrid example
applications:

- [cpp11][1]
- [cpp98][2]
- [csharp][3]
- [java][4]
- [python][5]

In a separate window, start the Client program:

```shell
dotnet run --project Client/Client.csproj
```

[1]: https://github.com/zeroc-ice/ice-demos/tree/3.7/cpp11/IceGrid/simple
[2]: https://github.com/zeroc-ice/ice-demos/tree/3.7/cpp98/IceGrid/simple
[3]: https://github.com/zeroc-ice/ice-demos/tree/3.7/csharp/IceGrid/simple
[4]: https://github.com/zeroc-ice/ice-demos/tree/3.7/java/IceGrid/simple
[5]: https://github.com/zeroc-ice/ice-demos/tree/3.7/python/IceGrid/simple
