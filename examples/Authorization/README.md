# Authorization

This demo demonstrates how token based authorization and authentication can be implemented with an interceptor and two
middleware. The token carries the name of the user and its administrative privilege.

The server dispatch pipeline is configured with two middleware:
- The `AuthenticationMiddleware` is responsible for validating the request's identity token field and setting the
  request's identity feature.
- The `AuthorizationMiddleware` is responsible for checking if the request's identity feature is authorized.

The client invocation pipeline is configured with an `AuthenticationInterceptor` interceptor, which is responsible for adding a request field with the encrypted identity token. The client obtains its identity token by authenticating itself with the `Authenticator` service.

The server supports two identity token types:
- A custom Slice based identity token encrypted with AES (the default).
- A JWT identity token.

## Running the example

First start the Server:

```shell
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client:

```shell
dotnet run --project Client/Client.csproj
```

The client first calls `GreetAsync` without an identity token and the server responds with a generic greeting.

Next, the client gets an identity token for the user `alice` and uses it to construct an authentication invocation
pipeline that adds the `alice` identity token to each request. The client then calls `GreetAsync` using the `alice`
authentication pipeline and receives a personalized message.

Next, the client calls `ChangeGreetingAsync` using the `alice` authentication pipeline to change the greeting. The user `alice` doesn't have administrative privilege so the invocation fails with a `DispatchException`.

Finally, the client authenticates the user `admin` and calls `ChangeGreetingAsync` using an `admin` authentication
pipeline. The call succeeds because the user `admin` has administrative privilege.

To use JTW token instead of AES tokens, start the Server with the `--jwt` argument:

```shell
dotnet run --project Server/Server.csproj --jwt
```
