// Copyright (c) ZeroC, Inc. All rights reserved.

using AuthorizationExample;
using IceRpc;

var helloService = new HelloService();
var sessionManager = new SessionManager();

var router = new Router();
router.Use(sessionManager.LoadSession);
router.Route("/admin", adminRouter =>
{
    adminRouter.Use(SessionManager.HasSession);
    adminRouter.Map("/", new AdminService(helloService));
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
