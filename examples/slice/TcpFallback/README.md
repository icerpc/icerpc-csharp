# TCP Fallback

This example shows how a client can attempt to connect to a server with QUIC and fallback to TCP when the QUIC
connection establishment fails - for example, because a middlebox between the client and server does not let QUIC
connections through.

The server program creates a server for QUIC and another server for TCP; both servers share the same dispatch pipeline.

The client program first attempts to establish a QUIC connection; if this connection establishment fails, it falls back
to a TCP connection.

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

In order to see the fallback to TCP, run the TCP fallback client with the server from the [../Secure] example.
