using System.Diagnostics;
using System.Text;

namespace DataGuard.Observability.Messaging;

public sealed record MessagePropagationContext(ActivityContext ActivityContext, IReadOnlyDictionary<string, string> Baggage, bool IsValid);

public static class W3CMessagePropagation
{
    private const string TraceParent = "traceparent";
    private const string TraceState = "tracestate";
    private const string BaggageHeader = "baggage";

    public static void Inject(Activity? activity, IDictionary<string, string> headers, IReadOnlySet<string>? allowedBaggage = null, int maxHeaderBytes = 8192)
    {
        ArgumentNullException.ThrowIfNull(headers);
        try
        {
            if (maxHeaderBytes <= 0) return;
            if (activity is null || activity.IdFormat != ActivityIdFormat.W3C || string.IsNullOrEmpty(activity.Id)) return;
            var additions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [TraceParent] = activity.Id };
            if (!string.IsNullOrWhiteSpace(activity.TraceStateString)
                && activity.TraceStateString!.Length <= 512
                && !activity.TraceStateString.Any(char.IsControl)) additions[TraceState] = activity.TraceStateString;
            if (allowedBaggage is not null)
            {
                var values = activity.Baggage
                    .Where(item => allowedBaggage.Contains(item.Key)
                        && !string.IsNullOrWhiteSpace(item.Value)
                        && item.Key.Length <= 64
                        && item.Value!.Length <= 256
                        && IsBaggageKey(item.Key)
                        && !item.Value.Any(char.IsControl))
                    .Select(item => $"{item.Key}={Uri.EscapeDataString(item.Value!)}")
                    .ToArray();
                if (values.Length > 0) additions[BaggageHeader] = string.Join(',', values);
            }
            var projected = HeaderBytes(headers.Where(item => !additions.ContainsKey(item.Key)).Concat(additions));
            if (projected > maxHeaderBytes) return;
            foreach (var item in additions) headers[item.Key] = item.Value;
        }
        catch (Exception)
        {
            // A read-only or faulty carrier is a telemetry propagation failure, not a business failure.
        }
    }

    public static MessagePropagationContext Extract(IReadOnlyDictionary<string, string> headers, IReadOnlySet<string>? allowedBaggage = null, int maxHeaderBytes = 8192)
    {
        ArgumentNullException.ThrowIfNull(headers);
        try
        {
            if (maxHeaderBytes <= 0 || HeaderBytes(headers) > maxHeaderBytes) return InvalidContext();
            var traceParent = Find(headers, TraceParent);
            var traceState = Find(headers, TraceState);
            var context = ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var parsed) ? parsed : default;
            var baggage = context == default ? new Dictionary<string, string>() : ParseBaggage(Find(headers, BaggageHeader), allowedBaggage);
            return new(context, baggage, context != default);
        }
        catch (Exception)
        {
            // A malformed or faulty carrier is untrusted input, never a business failure.
            return InvalidContext();
        }
    }

    private static MessagePropagationContext InvalidContext() => new(default, new Dictionary<string, string>(), false);

    private static string? Find(IReadOnlyDictionary<string, string> headers, string key)
        => headers.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static IReadOnlyDictionary<string, string> ParseBaggage(string? value, IReadOnlySet<string>? allowed)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (allowed is null || string.IsNullOrWhiteSpace(value)) return result;
        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0 || separator == entry.Length - 1) continue;
            var key = entry[..separator].Trim();
            var item = entry[(separator + 1)..].Trim();
            if (!allowed.Contains(key) || !IsBaggageKey(key) || item.Length > 256 || item.Any(char.IsControl) || !HasValidEscapes(item)) continue;
            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(item);
            }
            catch (UriFormatException)
            {
                continue;
            }
            if (decoded.Length <= 256 && !decoded.Any(char.IsControl)) result[key] = decoded;
        }
        return result;
    }

    private static bool IsBaggageKey(string value) => value.Length is > 0 and <= 64
        && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool HasValidEscapes(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%') continue;
            if (index + 2 >= value.Length || !IsHex(value[index + 1]) || !IsHex(value[index + 2])) return false;
            index += 2;
        }
        return true;
    }

    private static bool IsHex(char value) => value is >= '0' and <= '9'
        or >= 'a' and <= 'f'
        or >= 'A' and <= 'F';

    private static long HeaderBytes(IEnumerable<KeyValuePair<string, string>> headers)
    {
        long total = 0;
        foreach (var header in headers)
        {
            if (header.Key is null || header.Value is null) return long.MaxValue;
            total += Encoding.UTF8.GetByteCount(header.Key) + Encoding.UTF8.GetByteCount(header.Value) + 4;
            if (total > int.MaxValue) return long.MaxValue;
        }
        return total;
    }
}
