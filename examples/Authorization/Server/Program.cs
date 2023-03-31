// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

// The identity token is encrypted and decrypted with the AES symmetric encryption algorithm.
using var aes = Aes.Create();
aes.Padding = PaddingMode.Zeros;

var tokenHandler = new JwtSecurityTokenHandler();
var issuerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("A dummy secret key for Jwt test"));
var credentials = new SigningCredentials(issuerKey, SecurityAlgorithms.HmacSha256);

var router = new Router();

// Install a middleware to decrypt and decode the request's identity token and add an identity feature to the request's
// feature collection.
router.UseAuthentication(aes);

var chatbot = new Chatbot();
router.Map("/greeting", chatbot);

router.Map("/authenticator", new Authenticator(credentials));

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
