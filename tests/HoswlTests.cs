using System.IO.Pipes;
using System.Text.Json;
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class HoswlTests
{
    private static readonly HoswlMenu[] Sample =
    [
        new("file", "File",
        [
            new HoswlItem("file.new", "New Word", "N"),
            HoswlItem.Separator,
            new HoswlItem("file.off", "Off", Enabled: false),
        ]),
        new("go", "Go", [new HoswlItem("go.quiz", "Quiz", Check: true)]),
    ];

    [Fact]
    public void MenusSerialiseToTheProtocolShape()
    {
        using var doc = JsonDocument.Parse(HoswlClient.MenusJson(Sample));
        var menus = doc.RootElement;

        Assert.Equal(2, menus.GetArrayLength());
        var file = menus[0].GetProperty("items");
        Assert.Equal("file.new", file[0].GetProperty("id").GetString());
        Assert.Equal("N", file[0].GetProperty("key").GetString());
        Assert.False(file[0].TryGetProperty("check", out _));
        Assert.True(file[1].GetProperty("sep").GetBoolean());
        Assert.False(file[1].TryGetProperty("id", out _));
        Assert.False(file[2].GetProperty("enabled").GetBoolean());
        Assert.True(menus[1].GetProperty("items")[0].GetProperty("check").GetBoolean());
    }

    [Theory]
    [InlineData("{\"t\":\"click\",\"id\":\"file.new\"}", "file.new")]
    [InlineData("{\"t\":\"welcome\",\"v\":1}", null)]
    [InlineData("{\"t\":\"click\"}", null)]
    [InlineData("{\"t\":\"click\",\"id\":7}", null)]
    [InlineData("not json", null)]
    [InlineData("[1,2]", null)]
    public void OnlyWellFormedClicksAreReported(string line, string? expected) =>
        Assert.Equal(expected, HoswlClient.ParseClick(line));

    [Fact]
    public async Task SpeaksTheProtocolOverARealPipe()
    {
        var pipeName = "hoswl-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        using var client = new HoswlClient("com.example.test", "Test", "1.0", pipeName)
            { RetryInterval = TimeSpan.FromMilliseconds(50) };
        var clicked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Click += id => clicked.TrySetResult(id);
        client.SetMenus(Sample);
        client.SetEnabled(false);
        client.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await server.WaitForConnectionAsync(timeout.Token);
        // Not disposed: after "bye" the pipe is closed and a disposing flush would throw.
        var reader = new StreamReader(server);
        var writer = new StreamWriter(server) { AutoFlush = true, NewLine = "\n" };

        async Task<JsonElement> Next() =>
            JsonDocument.Parse((await reader.ReadLineAsync(timeout.Token))!).RootElement;

        var hello = await Next();
        Assert.Equal("hello", hello.GetProperty("t").GetString());
        Assert.Equal("com.example.test", hello.GetProperty("app").GetString());
        Assert.Equal(Environment.ProcessId, hello.GetProperty("pid").GetInt32());

        // The menu and switch set before connecting are replayed straight after hello.
        Assert.Equal("menu", (await Next()).GetProperty("t").GetString());
        var enable = await Next();
        Assert.Equal("enable", enable.GetProperty("t").GetString());
        Assert.False(enable.GetProperty("on").GetBoolean());

        await writer.WriteLineAsync("{\"t\":\"welcome\",\"v\":1,\"host\":\"Hisashi\",\"ver\":\"0.5.0\"}");
        await writer.WriteLineAsync("{\"t\":\"click\",\"id\":\"go.quiz\"}");
        Assert.Equal("go.quiz", await clicked.Task.WaitAsync(timeout.Token));

        // Read ahead as Hisashi does: Windows drops unread pipe data once the client closes.
        var bye = Next();
        client.Dispose();
        Assert.Equal("bye", (await bye).GetProperty("t").GetString());
    }
}
