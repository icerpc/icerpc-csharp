using Demo;
using IceRpc;

public class AuthenticationMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly string _password;

    /// <summary>Constructs a AuthenticationMiddleware middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public AuthenticationMiddleware(
        IDispatcher next, string password)
    {
        _next = next;
        _password = password;
    }

    public async ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancel = default)
    {
        Dictionary<string, string>? headers = request.Features.Get<Dictionary<string, string>>();
        if (headers is null)
        {
            throw new InvalidOperationException("No headers found");
        }
        else if (headers.TryGetValue("Authorization", out string? authorization) && authorization == _password)
        {
            return await _next.DispatchAsync(request, cancel);
        }
        else
        {
            throw new InvalidOperationException("User password incorrect");
        }
    }
}
