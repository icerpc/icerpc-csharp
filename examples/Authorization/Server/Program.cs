// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;

// Encodes the identity token field carried by requests as a JTW token.
//var authenticationBearer = new JwtAuthenticationBearer("A secret key for the authorization example");
using var authenticationBearer = new AesAuthenticationBearer();

var router = new Router();

// Install a middleware to decrypt and decode the request's identity token and add an identity feature to the request's
// feature collection.
router.UseAuthentication(authenticationBearer);

var chatbot = new Chatbot();
router.Map("/greeting", chatbot);

router.Map("/authenticator", new Authenticator(authenticationBearer));

router.Route("/greetingAdmin", adminRouter =>
{
    // Install an authorization middleware that checks if the caller is authorized to call the greeting admin service.
    adminRouter.UseAuthorization(identityFeature => identityFeature.IsAdmin);
    adminRouter.Map("/", new ChatbotAdmin(chatbot));
});

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
