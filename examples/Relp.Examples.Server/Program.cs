using System.Net;
using System.Net.Sockets;
using System.Text;
using Relp;
using ZstdSharp;

var port = args.Length > 0 && int.TryParse(args[0], out var parsedPort) ? parsedPort : 1601;
var bindAddress = args.ElementAtOrDefault(1)?.Equals("any", StringComparison.OrdinalIgnoreCase) == true
    ? IPAddress.Any
    : IPAddress.Loopback;
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var listener = new TcpListener(bindAddress, port);
listener.Start();
Console.Error.WriteLine($"RELP zstd NDJSON example server listening on {bindAddress}:{port}");
Console.Error.WriteLine("Press Ctrl+C to stop.");

try
{
    while (!cts.IsCancellationRequested)
    {
        var client = await listener.AcceptTcpClientAsync(cts.Token);
        _ = Task.Run(() => HandleClientAsync(client, cts.Token), cts.Token);
    }
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    // Graceful shutdown requested by Ctrl+C.
}
finally
{
    listener.Stop();
}

static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
{
    using var clientRegistration = client;
    await using var stream = client.GetStream();
    using var decompressor = new Decompressor();

    var parser = new RelpParser();
    var pending = Array.Empty<byte>();
    var buffer = new byte[4096];

    while (!cancellationToken.IsCancellationRequested)
    {
        if (pending.Length > 0)
        {
            parser.Parse(pending);
            pending = Array.Empty<byte>();
        }

        while (!parser.IsComplete)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) return;
            parser.Parse(buffer.AsSpan(0, read));
        }

        pending = parser.RemainingBytes;
        var frame = parser.ToFrame();
        parser = new RelpParser();

        switch (frame.Command)
        {
            case RelpCommand.Open:
                await SendAckAsync(stream, frame.TransactionId, "200 OK\nrelp_version=0\ncommands=syslog", cancellationToken);
                break;
            case RelpCommand.Syslog:
                PrintCompressedJsonLines(decompressor, frame.Buffer);
                await SendAckAsync(stream, frame.TransactionId, "200 OK", cancellationToken);
                break;
            case RelpCommand.Close:
                await SendAckAsync(stream, frame.TransactionId, "200 OK", cancellationToken);
                return;
            default:
                await SendAckAsync(stream, frame.TransactionId, "500 unsupported command", cancellationToken);
                break;
        }
    }
}

static void PrintCompressedJsonLines(Decompressor decompressor, byte[] compressed)
{
    var payload = Encoding.UTF8.GetString(decompressor.Unwrap(compressed));
    foreach (var line in payload.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        Console.WriteLine($"{DateTimeOffset.UtcNow:O}: {line}");
    }
}

static async Task SendAckAsync(NetworkStream stream, int transactionId, string message, CancellationToken cancellationToken)
{
    var frame = RelpFrameTx.FromCommandAndMessage(RelpCommand.Response, Encoding.UTF8.GetBytes(message));
    await stream.WriteAsync(frame.ToByteArray(transactionId), cancellationToken);
    await stream.FlushAsync(cancellationToken);
}
