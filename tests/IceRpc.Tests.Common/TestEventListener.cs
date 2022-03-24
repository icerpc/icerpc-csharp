// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Tracing;

namespace IceRpc.Tests;

/// <summary>An event listener used by unit tests to wait for events published by an event source.</summary>
public class TestEventListener : EventListener
{
    public List<(string Key, string Value)> ExpectedEventCounters { get; }
    public List<(string Key, string Value)> ReceivedEventCounters { get; } = new();
    private EventSource? _eventSource;
    private readonly string _sourceName;
    private readonly SemaphoreSlim _semaphore;
    private readonly object _mutex = new(); // protects ReceivedEventCounters

    public TestEventListener(string sourceName, params (string Key, string Value)[] expectedCounters)
    {
        ExpectedEventCounters = new List<(string, string)>(expectedCounters);
        _semaphore = new SemaphoreSlim(0);
        _sourceName = sourceName;
    }

    public async Task WaitForCounterEventsAsync()
    {
        for (int i = 0; i < ExpectedEventCounters.Count; ++i)
        {
            await _semaphore.WaitAsync();
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // OnEventSourceCreated can be called as soon as the base constructor runs and before
        // _sourceName is assigned, if that is the case we ignore the source.
        if (_sourceName == null)
        {
            return;
        }

        if (_sourceName == eventSource.Name)
        {
            lock (_mutex)
            {
                if (_eventSource == null)
                {
                    _eventSource = eventSource;
                    EnableEvents(
                        eventSource,
                        EventLevel.LogAlways,
                        EventKeywords.All,
                        new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "0.001" });
                }
            }
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventSource == _eventSource && eventData.EventId == -1) // counter event
        {
            var eventPayload = (IDictionary<string, object?>)eventData.Payload![0]!;

            string name = "";
            if (eventPayload.TryGetValue("Name", out object? nameValue))
            {
                name = nameValue?.ToString() ?? "";
            }

            string value = "";
            if (eventPayload.TryGetValue("Increment", out object? incrementValue))
            {
                value = incrementValue?.ToString() ?? "";
            }
            else if (eventPayload.TryGetValue("Mean", out object? meanValue))
            {
                value = meanValue?.ToString() ?? "";
            }

            foreach ((string Key, string Value) entry in ExpectedEventCounters)
            {
                if (entry.Key == name && entry.Value == value && !ReceivedEventCounters.Contains(entry))
                {
                    lock (_mutex)
                    {
                        ReceivedEventCounters.Add(entry);
                    }
                    _semaphore.Release();
                    break;
                }
            }
        }
    }
}
