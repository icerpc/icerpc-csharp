# Discriminated union

The Discriminated union example shows how to define discriminated unions in Slice, and then how to use the C# code
generated for the Slice type.

You define a discriminated union in Slice by defining an enum without an underlying type. Each enumerator of such an
enum can then define 0 or more fields.

The Slice code generator for C# maps such a Slice enum to a C# [union] with a nested record class for each variant.

This example uses QUIC, IceRPC's default multiplexed transport. On Linux and macOS, QUIC requires extra setup
steps; see .NET's [QUIC platform dependencies][quic-platform].

You can build the client and server applications with:

``` shell
dotnet build
```

First start the Server program:

```shell
cd Server
dotnet run
```

In a separate terminal, start the Client program:

```shell
cd Client
dotnet run
```

[quic-platform]: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview#platform-dependencies
[union]: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/union
