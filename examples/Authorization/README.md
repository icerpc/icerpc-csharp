# Authorization

This example application illustrates how to create an authentication interceptor, authentication middleware and
authorization middleware that can be used to authorize requests.

The server is configured with two middleware: `AuthenticationMiddleware` and `AuthorizationMiddleware`. The first
middleware is responsible for decrypting an authentication token from the request field and storing it in a
corresponding request feature. The second middleware is responsible for checking if the authentication feature is
present in the corresponding request feature and to check if the request is authorized.

The client is configured with an `AuthenticationInterceptor` interceptor. The interceptor is responsible for adding a
encrypted authentication token to a request field. The authentication token is returned by an `Authenticator` service after
authenticating the client with a login name and password.

## Running the example

First start the Server:

```shell
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client:

```shell
dotnet run --project Client/Client.csproj
```

The client first calls `SayHelloAsync` without an authentication token and the server responds with generic a greeting.

Next, the client gets an authentication token for the user `friend` and uses it to construct an authentication
invocation pipeline that adds the `friend` token to each request. The client then calls `SayHelloAsync` using this
`friend` authentication pipeline and receives a personalized message.

Next, the client calls `ChangeGreetingAsync` using the `friend` authentication pipeline to change the greeting. The client doesn't have administrative privilege so the invocation fails with a `DispatchException`.

Finally, the client authenticates the user `admin` and calls `ChangeGreetingAsync` using an `admin` authentication
pipeline to change the greeting.
