// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;
using System.Security.Cryptography;

// Use AES symmetric encryption to crypt the token.
using var aes = Aes.Create();
aes.Padding = PaddingMode.Zeros;

var chatbot = new Chatbot();

var router = new Router();

// Install a middleware to get and decrypt the authentication token from a request and to add the authentication feature
// to the request's feature collection.
router.UseAuthentication(aes);

router.Route("/helloAdmin", adminRouter =>
{
    // Install an authorization middleware to check if the caller is authorized to call the hello admin service.
    adminRouter.UseAuthorization(authenticationFeature => authenticationFeature.IsAdmin);
    adminRouter.Map("/", new ChatbotAdmin(chatbot));
});

router.Map("/authenticator", new Authenticator(aes));
router.Map("/hello", chatbot);

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
