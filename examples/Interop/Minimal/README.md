This example application illustrates how IceRPC can interact with ZeroC Ice using the `ice` protocol and `Slice1`
encoding. The server application uses ZeroC Ice and the client application uses IceRPC.

For build instructions check the top-level [README.md](../../.../README.md).

First, start a hello server by following the instructions provided in any of the following `minimal` ZeroC Ice example
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

[1]: https://github.com/zeroc-ice/ice-demos/tree/3.7/cpp11/Ice/minimal
[2]: https://github.com/zeroc-ice/ice-demos/tree/3.7/cpp98/Ice/minimal
[3]: https://github.com/zeroc-ice/ice-demos/tree/3.7/csharp/Ice/minimal
[4]: https://github.com/zeroc-ice/ice-demos/tree/3.7/java/Ice/minimal
[5]: https://github.com/zeroc-ice/ice-demos/tree/3.7/python/Ice/minimal
