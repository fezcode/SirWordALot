using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace SirWordALot.Core;

/// <summary>A top-level menu published to Hisashi's menubar.</summary>
public sealed record HoswlMenu(string Id, string Label, IReadOnlyList<HoswlItem> Items);

/// <summary>A leaf item or a separator. <see cref="Check"/> set makes it checkable.</summary>
public sealed record HoswlItem(string Id, string Label, string? Key = null, bool? Check = null, bool Enabled = true)
{
    public static readonly HoswlItem Separator = new("", "");
    public bool IsSeparator => Id.Length == 0;
}

/// <summary>
/// Client for the Hisashi OS Window Layer (hoswl, hisashi/docs/hoswl-protocol.md):
/// one named pipe, one JSON object per line. Connects on a background task,
/// retries while Hisashi is absent, re-sends the cached menu after a reconnect,
/// and raises <see cref="Click"/> on a background thread.
/// </summary>
public sealed class HoswlClient(string appId, string name, string version, string pipeName = "hoswl") : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly object _lock = new();
    private Channel<byte[]>? _out;
    private Task? _writer;
    private string? _menuLine;
    private bool _enabled = true;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Raised on a background thread with the clicked item id.</summary>
    public event Action<string>? Click;

    public bool IsConnected { get; private set; }
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(2);

    public void Start()
    {
        lock (_lock)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => LoopAsync(token), token);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_lock)
        {
            (cts, loop) = (_cts, _loop);
            (_cts, _loop) = (null, null);
        }
        if (cts is null) return;

        Send("{\"t\":\"bye\"}\n");
        Channel<byte[]>? queue;
        Task? writer;
        lock (_lock) (queue, writer) = (_out, _writer);
        queue?.Writer.TryComplete();
        try { writer?.Wait(500); } catch (AggregateException) { } // let "bye" leave first
        cts.Cancel();
        try { loop?.Wait(1000); } catch (AggregateException) { }
        cts.Dispose();
    }

    /// <summary>Replaces the whole menu tree. Cached, so a reconnect re-sends it.</summary>
    public void SetMenus(IReadOnlyList<HoswlMenu> menus)
    {
        var line = "{\"t\":\"menu\",\"menus\":" + MenusJson(menus) + "}\n";
        lock (_lock) _menuLine = line;
        Send(line);
    }

    /// <summary>The integration switch: off keeps the pipe open but Hisashi shows nothing of ours.</summary>
    public void SetEnabled(bool on)
    {
        lock (_lock)
        {
            if (_enabled == on) return;
            _enabled = on;
        }
        Send(on ? "{\"t\":\"enable\",\"on\":true}\n" : "{\"t\":\"enable\",\"on\":false}\n");
    }

    public static string MenusJson(IReadOnlyList<HoswlMenu> menus)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartArray();
            foreach (var menu in menus)
            {
                w.WriteStartObject();
                w.WriteString("id", menu.Id);
                w.WriteString("label", menu.Label);
                w.WriteStartArray("items");
                foreach (var item in menu.Items)
                {
                    w.WriteStartObject();
                    if (item.IsSeparator)
                    {
                        w.WriteBoolean("sep", true);
                    }
                    else
                    {
                        w.WriteString("id", item.Id);
                        w.WriteString("label", item.Label);
                        if (item.Key is not null) w.WriteString("key", item.Key);
                        if (item.Check is { } check) w.WriteBoolean("check", check);
                        if (!item.Enabled) w.WriteBoolean("enabled", false);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The clicked id if the line is a well-formed click, otherwise null.
    /// Unknown and malformed lines are ignored by design.</summary>
    public static string? ParseClick(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.String || t.GetString() != "click")
                return null;
            return root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } value ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Sends never touch the pipe on the caller's thread: lines are queued and one
    // writer per connection drains them, so the UI thread never waits on Hisashi.
    private void Send(string line)
    {
        Channel<byte[]>? queue;
        lock (_lock) queue = _out;
        queue?.Writer.TryWrite(Utf8.GetBytes(line));
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                // Fails fast when Hisashi is not running; we try again after RetryInterval.
                await pipe.ConnectAsync(250, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                if (!await SleepAsync(token).ConfigureAwait(false)) return;
                continue;
            }

            var queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
            var writer = Task.Run(async () =>
            {
                try
                {
                    await foreach (var chunk in queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                        await pipe.WriteAsync(chunk, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    pipe.Dispose(); // the reader notices and reconnects
                }
            }, CancellationToken.None);

            lock (_lock) (_out, _writer, IsConnected) = (queue, writer, true);
            try
            {
                Send("{\"t\":\"hello\",\"v\":1,\"app\":" + JsonSerializer.Serialize(appId)
                     + ",\"name\":" + JsonSerializer.Serialize(name)
                     + ",\"ver\":" + JsonSerializer.Serialize(version)
                     + ",\"pid\":" + Environment.ProcessId + "}\n");
                string? menu;
                bool enabled;
                lock (_lock) (menu, enabled) = (_menuLine, _enabled);
                if (menu is not null) Send(menu);
                if (!enabled) Send("{\"t\":\"enable\",\"on\":false}\n");

                using var reader = new StreamReader(pipe, Utf8);
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line is null) break;
                    if (ParseClick(line) is { } id) Click?.Invoke(id);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Hisashi went away or we are shutting down; either way, reconnect or exit below.
            }
            finally
            {
                lock (_lock) (_out, _writer, IsConnected) = (null, null, false);
                queue.Writer.TryComplete();
                await pipe.DisposeAsync().ConfigureAwait(false);
                await writer.ConfigureAwait(false);
            }

            if (!await SleepAsync(token).ConfigureAwait(false)) return;
        }
    }

    private async Task<bool> SleepAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(RetryInterval, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
