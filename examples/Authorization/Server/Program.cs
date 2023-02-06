// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;

var hello = new Hello();
var tokenStore = new TokenStore();

var router = new Router();

// Loads the session token from the request and adds the session feature to the request's feature collection
router.UseLoadSession(tokenStore);

router.Route("/helloAdmin", adminRouter =>
{
    // Requires the session feature to be present in the request's feature collection.
    adminRouter.UseHasSession();
    adminRouter.Map("/", new HelloAdmin(hello));
});

router.Map("/sessionManager", new SessionManager(tokenStore));
router.Map("/hello", hello);

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
