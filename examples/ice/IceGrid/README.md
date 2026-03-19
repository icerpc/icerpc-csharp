# IceGrid

This example application illustrates how an IceRPC client can call services in a Ice server managed by IceGrid.
The server application uses ZeroC Ice, and the client application uses IceRPC.

You can build the client applications with:

``` shell
dotnet build
```

First, start an IceGrid registry and node by following the instructions provided in any of the following `greeter`
IceGrid demo applications:

- [cpp][1]
- [csharp][2]
- [java][3]
- [python][4]

In a separate terminal, start the Client program:

```shell
cd Client
dotnet run
```

[1]: https://github.com/zeroc-ice/ice-demos/tree/3.8/cpp/IceGrid/greeter
[2]: https://github.com/zeroc-ice/ice-demos/tree/3.8/csharp/IceGrid/Greeter
[3]: https://github.com/zeroc-ice/ice-demos/tree/3.8/java/IceGrid/greeter
[4]: https://github.com/zeroc-ice/ice-demos/tree/3.8/python/IceGrid/greeter
