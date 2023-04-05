// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;

IBearerAuthenticationHandler? bearerAuthenticationHandler = null;
if (args.Length == 0)
{
    bearerAuthenticationHandler = new AesBearerAuthenticationHandler();
}
else if (args.Length == 1 && args[0] == "--jwt")
{
    bearerAuthenticationHandler = new JwtBearerAuthenticationHandler("A secret key for the authorization example");
}
else
{
    Console.WriteLine($"Invalid server arguments.");
    return;
}

// Dispose the bearer authentication handler if it's disposable.
using var disposable = bearerAuthenticationHandler as IDisposable;

var router = new Router();

// Install a middleware to validate the request's identity token and add an identity feature to the request's feature
// collection.
router.UseAuthentication(bearerAuthenticationHandler);

var chatbot = new Chatbot();
router.Map("/greeter", chatbot);

router.Map("/authenticator", new Authenticator(bearerAuthenticationHandler));

router.Route("/greeterAdmin", adminRouter =>
{
    // Install an authorization middleware that checks if the caller is authorized to call the greeter admin service.
    adminRouter.UseAuthorization(identityFeature => identityFeature.IsAdmin);
    adminRouter.Map("/", new ChatbotAdmin(chatbot));
});

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
