# Interop Greeter

This example application illustrates how IceRPC can communicate with ZeroC Ice using the `ice` protocol and `Slice1`
mode.

First, build the client and server applications for IceRPC with:

``` shell
dotnet build
```

Then, build the client and/or server for the Ice 3.8 `greeter` demo in the language of your choice, for example
the [Ice C++ Greeter].

You can then run any combination of Ice-based and IceRPC-based client and server applications.

The commands below start the IceRPC-based client application:

```shell
cd Client
dotnet run
```

The commands below start the IceRPC-based server application:

```shell
cd Server
dotnet run
```

[Ice C++ Greeter]: https://github.com/zeroc-ice/ice-demos/tree/3.8/cpp/Ice/greeter
