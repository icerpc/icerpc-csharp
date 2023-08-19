# Minimal

This example application illustrates how IceRPC can communicate with ZeroC Ice using the `ice` protocol and `Slice1`
encoding.

First, build the client and server applications for IceRPC with:

``` shell
dotnet build
```

Then, build the client and/or server for any of the following `minimal` ZeroC Ice example applications:

- [cpp11][1]
- [cpp98][2]
- [csharp][3]
- [java][4]
- [objective-c][5]
- [python][6]
- [swift][7]

You can then run any combination of Ice-based and IceRPC-based client and server applications.

The commands below starts the IceRPC-based client application:

```shell
cd Client
dotnet run
```

The commands below starts the IceRPC-based server application:

```shell
cd Server
dotnet run
```

[1]: https://github.com/zeroc-ice/ice-demos/tree/3.7/cpp11/Ice/minimal
[2]: https://github.com/zeroc-ice/ice-demos/tree/3.7/cpp98/Ice/minimal
[3]: https://github.com/zeroc-ice/ice-demos/tree/3.7/csharp/Ice/minimal
[4]: https://github.com/zeroc-ice/ice-demos/tree/3.7/java/Ice/minimal
[5]: https://github.com/zeroc-ice/ice-demos/tree/3.7/objective-c/Ice/minimal
[6]: https://github.com/zeroc-ice/ice-demos/tree/3.7/python/Ice/minimal
[7]: https://github.com/zeroc-ice/ice-demos/tree/3.7/swift/Ice/minimal
