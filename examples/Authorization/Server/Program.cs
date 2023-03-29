// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;
using System.Security.Cryptography;

// The identity token is encrypted and decrypted with the AES symmetric encryption algorithm.
using var aes = Aes.Create();
aes.Padding = PaddingMode.Zeros;

var router = new Router();

// Install a middleware to decrypt and decode the request's identity token and add an identity feature to the request's
// feature collection.
router.UseAuthentication(aes);

var chatbot = new Chatbot();
router.Map("/greeting", chatbot);

router.Map("/authenticator", new Authenticator(aes));

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
