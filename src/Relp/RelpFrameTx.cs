using System.Text;

namespace Relp;

/// <summary>A RELP frame ready for transmission.</summary>
public sealed class RelpFrameTx
{
    private readonly byte[] _message;

    public RelpFrameTx(RelpCommand command, byte[]? message = null)
    {
        Command = command;
        _message = message?.ToArray() ?? Array.Empty<byte>();
    }

    public RelpCommand Command { get; }
    public byte[] Message => _message.ToArray();

    public static RelpFrameTx FromMessage(byte[] message) => new(RelpCommand.Syslog, message);

    public static RelpFrameTx FromCommand(RelpCommand command) => new(command);

    public static RelpFrameTx FromCommandAndMessage(RelpCommand command, byte[] message) => new(command, message);

    public byte[] ToByteArray(int transactionId = TxId.MinValue)
    {
        if (transactionId is < TxId.MinValue or > TxId.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionId), $"RELP transaction id must be between {TxId.MinValue} and {TxId.MaxValue}.");
        }

        var header = Encoding.ASCII.GetBytes($"{transactionId} {Command.ToProtocolString()} {_message.Length}");
        if (_message.Length == 0)
        {
            return [.. header, (byte)'\n'];
        }

        return [.. header, (byte)' ', .. _message, (byte)'\n'];
    }

    public string ToProtocolString(int transactionId = TxId.MinValue)
    {
        var frame = ToByteArray(transactionId);
        return Encoding.UTF8.GetString(frame, 0, frame.Length - 1);
    }
}
