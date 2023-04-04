// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>A ChatbotAdmin is an IceRPC service that implements the Slice interface 'GreeterAdmin'.</summary>
internal class ChatbotAdmin : Service, IGreeterAdminService
{
    private readonly Chatbot _chatbot;

    public ValueTask ChangeGreetingAsync(
        string greeting,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        string who = features.Get<IIdentityFeature>()?.Name ?? "stranger";
        Console.WriteLine($"Dispatching changeGreeting request {{ name = '{who}' greeting = '{greeting}' }}");
        _chatbot.Greeting = greeting;
        return default;
    }

    internal ChatbotAdmin(Chatbot chatbot) => _chatbot = chatbot;
}
