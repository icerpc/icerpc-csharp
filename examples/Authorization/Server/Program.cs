// Copyright (c) ZeroC, Inc. All rights reserved.

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

// Create a task completion source to keep running until Ctrl+C is pressed.
var cancelKeyPressed = new TaskCompletionSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = cancelKeyPressed.TrySetResult();
};

server.Listen();
await cancelKeyPressed.Task;
await server.ShutdownAsync();
