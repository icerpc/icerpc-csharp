# Authorization

This demo demonstrates how token based authorization and authentication can be implemented with an interceptor and two
middleware. The token provides identify information.

The server is configured with two middleware: `AuthenticationMiddleware` and `AuthorizationMiddleware`. The first
middleware is responsible for decrypting an identity token from the request field and storing it in a corresponding
request feature. The second middleware is responsible for checking if the identity feature is present in the
corresponding request feature and it checks if the request is authorized.

The client is configured with an `AuthenticationInterceptor` interceptor. The interceptor is responsible for adding the
encrypted identity token to a request field. The identity token is returned by an `Authenticator` service after
authenticating the client with a login name and password.

The server supports two token types:
- a JWT identity token
- a custom Slice based identity token encoded with AES.

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

Next, the client gets an identity token for the user `friend` and uses it to construct an authentication invocation
pipeline that adds the `friend` identity token to each request. The client then calls `GreetAsync` using the `friend`
authentication pipeline and receives a personalized message.

Next, the client calls `ChangeGreetingAsync` using the `friend` authentication pipeline to change the greeting. The user `friend` doesn't have administrative privilege so the invocation fails with a `DispatchException`.

Finally, the client authenticates the user `admin` and calls `ChangeGreetingAsync` using an `admin` authentication
pipeline. The call succeeds because the user `admin` has administrative privilege.

To use JTW token instead of AES tokens, start the Server with the `--jwt` argument:

```shell
dotnet run --project Server/Server.csproj --jwt
```
