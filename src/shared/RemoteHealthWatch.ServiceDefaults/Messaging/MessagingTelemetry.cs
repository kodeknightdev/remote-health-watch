using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace RemoteHealthWatch.ServiceDefaults.Messaging;

public static class MessagingTelemetry
{
    public const string SourceName = "RemoteHealthWatch.Messaging";
    public static readonly ActivitySource Source = new(SourceName);

    public static ActivityContext ExtractParent(Headers? headers)
    {
        var header = headers?.FirstOrDefault(h => h.Key == "traceparent");
        if (header is null) return default;
        return ActivityContext.TryParse(Encoding.UTF8.GetString(header.GetValueBytes()), null, out var ctx) ? ctx : default;
    }
}
