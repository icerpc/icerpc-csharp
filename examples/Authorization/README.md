# Authorization

This example application illustrates how to create an authorization interceptor and middleware that can be used
to authorize requests.

The server is configured with two middleware components: `LoadSession` and `HasSession`. The first middleware is
responsible for loading the session from the request field and storing it in a corresponding `Feature`. The second
middleware is responsible for checking if the session is present in the `Feature` and returning an error if it is not.

The client has an authorization pipeline that is responsible for adding the session token as a request field.

## Running the example

First start the Server:

```shell
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client:

```shell
dotnet run --project Client/Client.csproj
```

The client first calls `SayHello` without a session token and the server responds with generic greeting.

Next, the client gets an authentication token and uses it to construct an authenticated pipeline. The client then
calls `SayHello` using the authenticated pipeline and receives a personalized message.

Finally, the client calls `ChangeGreeting` using the authenticated pipeline to change the greeting and then calls
`SayHello` a final time.
