using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PCMonitor.Service.Alerts;

public sealed class LiveEventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<LiveEventEnvelope>> _subscribers = new();

    public LiveEventSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<LiveEventEnvelope>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return new LiveEventSubscription(channel.Reader, () => _subscribers.TryRemove(id, out _));
    }

    public void Publish(LiveEventEnvelope message)
    {
        foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(message);
    }
}

public sealed class LiveEventSubscription(ChannelReader<LiveEventEnvelope> events, Action dispose) : IDisposable
{
    public ChannelReader<LiveEventEnvelope> Events { get; } = events;
    public void Dispose() => dispose();
}
