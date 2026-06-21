using System.Text;
using System.Text.Json;
using ZstdSharp;

namespace Relp.Examples.Client
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var host = args.ElementAtOrDefault(0) ?? "127.0.0.1";
            var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 1601;
            var count = args.Length > 2 && int.TryParse(args[2], out var parsedCount) ? parsedCount : 5_000_000;

            await using var connection = new RelpConnection(host, port);
            await connection.ConnectAsync();

            var session = new RelpSession(connection);
            using var compressor = new Compressor(3);

            await session.OpenAsync();
            Console.WriteLine("Started new session.");
            Console.WriteLine("Forwarding logs...");

            for (var index = 1; index <= count; index++)
            {
                var line = JsonSerializer.Serialize(new {
                    timestamp = DateTimeOffset.UtcNow,
                    level = "info",
                    message = $"hello from zstd-compressed NDJSON #{index}",
                    sequence = index
                });

                var compressed = compressor.Wrap(Encoding.UTF8.GetBytes(line)).ToArray();
                await session.SendMessageAsync(compressed);
            }
            Console.WriteLine("Log sending completed.");
            Console.WriteLine("Closing session...");
            await session.CloseAsync();
        }
    }
}