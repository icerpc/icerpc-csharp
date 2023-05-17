using System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Core;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
        listenOptions.UseHttps();
    });
});
builder.WebHost.UseQuic(options => options.MaxBidirectionalStreamCount = 1);
WebApplication app = builder.Build();

int i = 0;
app.MapPut(
    "/",
    async context =>
    {
        Console.WriteLine($"Accepting request {i++}");

        // Send response.
        context.Response.StatusCode = 200;
        await context.Response.CompleteAsync();

        // Read the request body.
        PipeReader reader = context.Request.BodyReader;
        ReadResult readResult = await reader.ReadAtLeastAsync(10);
        await Task.Delay(10); // Slow query
        reader.Complete();
    });

app.Run();
