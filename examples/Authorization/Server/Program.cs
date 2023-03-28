// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;
using System.Security.Cryptography;

// The authentication token is encrypted and decrypted with the AES symmetric encryption algorithm.
using var aes = Aes.Create();
aes.Padding = PaddingMode.Zeros;

var chatbot = new Chatbot();

var router = new Router();

// Install a middleware to decrypt and decode the request's authentication token and add an authentication feature to
// the request's feature collection.
router.UseAuthentication(aes);

router.Route("/helloAdmin", adminRouter =>
{
    // Install an authorization middleware that checks if the caller is authorized to call the hello admin service.
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
