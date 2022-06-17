This application illustrates how to use the Jwt  interceptor and middleware to authenticate clients acrross services,
the Auth service issues Jwt tokens when a user signs-in, and the Hello service validate the token before letting the
user access it, the Auth and Hello servers use a symetric key derived from a shared secret to sign and validate the
Jwt tokens.

For build instructions check the top-level [README.md](../../README.md).

First start the Auth Server program:

```
dotnet run --project AuthServer/AuthServer.csproj
```

Next start the Hello Server program:

```
dotnet run --project HelloServer/HelloServer.csproj
```

An finally start the Client program:

```
dotnet run --project Client/Client.csproj
```
