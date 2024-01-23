# Discriminated union

The Discriminated union example shows how to define discriminated unions in Slice, and then how to use the C# code
generated for the Slice type.

You define a discriminated union in Slice by defining an enum without an underlying type. Each enumerator of such an
enum can then define 0 or more fields.

Since C# does not provide native support for discriminated unions, the Slice code generator for C# maps such a Slice
enum to several C# record classes and relies on the [Dunet] source generator to provide various methods for these record
classes.

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

[Dunet]: https://github.com/domn1995/dunet
