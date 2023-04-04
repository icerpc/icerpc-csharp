# Authorization

This example application illustrates how to create an authorization interceptor and middleware that can be used
to authorize requests.

The server is configured with two middleware components: `LoadSession` and `HasSession`. The first middleware is
responsible for loading the session from the request field and storing it in a corresponding request feature. The second
middleware is responsible for checking if the session is present in the corresponding request feature and
returning an error if it is not.

The client creates an authorization pipeline that is responsible for adding the session token as a request field.

## Running the example

First start the Server:

```shell
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client:

```shell
dotnet run --project Client/Client.csproj
```

The client first calls `GreetAsync` without a session token and the server responds with generic a greeting.

Next, the client gets an authentication token and uses it to construct an authenticated invocation pipeline that adds
the token to each request. The client then calls `GreetAsync` using the authenticated pipeline and receives
a personalized message.

Finally, the client calls `ChangeGreetingAsync` using the authenticated pipeline to change the greeting and then calls
`GreetAsync` a final time.
