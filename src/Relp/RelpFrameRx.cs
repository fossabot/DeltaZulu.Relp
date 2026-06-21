using System.Text;

namespace Relp;

/// <summary>A received RELP frame.</summary>
public sealed class RelpFrameRx
{
    private readonly byte[] _buffer;

    public RelpFrameRx(int transactionId, RelpCommand command, int length, byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (transactionId is < TxId.MinValue or > TxId.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionId), $"RELP transaction id must be between {TxId.MinValue} and {TxId.MaxValue}.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "RELP payload length must not be negative.");
        }

        if (buffer.Length != length)
        {
            throw new ArgumentException("Buffer length must match RELP payload length.", nameof(buffer));
        }

        TransactionId = transactionId;
        Command = command;
        Length = length;
        _buffer = buffer.ToArray();
    }

    public int TransactionId { get; }
    public RelpCommand Command { get; }
    public int Length { get; }
    public byte[] Buffer => _buffer.ToArray();

    public int GetResponseCode()
    {
        var text = GetData().Trim();
        var firstToken = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!int.TryParse(firstToken, out var code) || code is < 100 or > 999)
        {
            throw new FormatException("Invalid RELP response code.");
        }
        return code;
    }

    public string GetData() => Encoding.UTF8.GetString(_buffer);
}
