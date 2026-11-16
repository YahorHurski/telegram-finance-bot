using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Captures log events as the exact text prod writes to stdout (same formatter), so the privacy test sees what docker logs would
public sealed class CapturingSink : ILogEventSink
{
    readonly ConcurrentQueue<string> _lines = new();
    readonly RenderedCompactJsonFormatter _fmt = new();

    public void Emit(LogEvent e)
    {
        var w = new StringWriter();
        _fmt.Format(e, w);
        _lines.Enqueue(w.ToString());
    }

    public string All => string.Join("\n", _lines);
    public int Count => _lines.Count;
}
