namespace DataGuard.Core.Reporting;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Emits safe, line-delimited JSON progress events to an operator-provided writer.
/// </summary>
public sealed class ProgressEmitter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TextWriter writer;
    private readonly object gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressEmitter"/> class.
    /// </summary>
    /// <param name="writer">The destination for line-delimited JSON events.</param>
    /// <param name="enabled">Whether events should be emitted.</param>
    public ProgressEmitter(TextWriter writer, bool enabled = false)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.Enabled = enabled;
    }

    /// <summary>
    /// Gets a value indicating whether progress events are emitted.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// Writes one progress event without allocating serialization state when disabled.
    /// </summary>
    /// <param name="progressEvent">The event to emit.</param>
    public void Emit(ProgressEvent progressEvent)
    {
        if (!this.Enabled)
        {
            return;
        }

        lock (this.gate)
        {
            this.writer.WriteLine(JsonSerializer.Serialize(progressEvent, SerializerOptions));
            this.writer.Flush();
        }
    }
}
