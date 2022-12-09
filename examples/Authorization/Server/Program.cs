// Copyright (c) ZeroC, Inc. All rights reserved.

using AuthorizationExample;
using IceRpc;

var helloService = new HelloService();
var sessionManager = new SessionManager();

var router = new Router();

// Loads the session token from the request and adds the session feature to the request's feature collection
router.Use((next) => new LoadSessionMiddleware(next, sessionManager));

router.Route("/admin", adminRouter =>
{
    // Requires the session feature to be present in the request's feature collection.
    adminRouter.Use((next) => new HasSessionMiddleware(next));
    adminRouter.Map("/", new HelloAdminService(helloService));
});

router.Map("/session", new SessionService(sessionManager));
router.Map("/hello", helloService);

await using var server = new Server(router);

// Destroy the server on Ctrl+C or Ctrl+Break
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
