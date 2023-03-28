// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>A ChatbotAdmin is an IceRPC service that implements the Slice interface 'HelloAdmin'.</summary>
internal class ChatbotAdmin : Service, IHelloAdminService
{
    private readonly Chatbot _chatbot;

    internal ChatbotAdmin(Chatbot chatbot) => _chatbot = chatbot;

    /// <inheritdoc/>
    public ValueTask ChangeGreetingAsync(
        string greeting,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        string who = features.Get<IAuthenticationFeature>()?.Name ?? "stranger";
        Console.WriteLine($"Dispatching changeGreeting request {{ name = '{who}' greeting = '{greeting}' }}");
        _chatbot.Greeting = greeting;
        return default;
    }
}
