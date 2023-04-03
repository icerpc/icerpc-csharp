// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>A service that implements Slice interface GreeterAdmin. It is used to change the greeting and requires
/// callers to be authenticated.</summary>
internal class ChatbotAdmin : Service, IGreeterAdminService
{
    private readonly Chatbot _chatbot;

    internal ChatbotAdmin(Chatbot chatbot) => _chatbot = chatbot;

    public ValueTask ChangeGreetingAsync(
        string greeting,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        _chatbot.Greeting = greeting;
        return default;
    }
}
