# Authorization

This example application illustrates how to use an authorization interceptor and middleware to authorize requests.

The server is configured with two middleware components: `LoadSession` and `HasSession`. The first middleware is
responsible for loading the session from the request field and storing it in a corresponding `Feature`. The second
middleware is responsible for checking if the session is present in the `Feature` and returning an error if it is not.

The client is configured with an authorization interceptor that is responsible for adding the session
token (if it exists) as a request field.

## Running the example

First start the Server:

```shell
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client

```shell
dotnet run --project Client/Client.csproj
```

The client will first send a `SayHello` without a session token and the server will return `Hello, stranger!`.
Next, the client will attempt to change the greeting but get an `Unauthorized` failure because the session
token is missing.

After logging in, the client will now receive a personalized greeting when calling `SayHello` and will be able to change
the greeting.
