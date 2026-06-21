using System.Buffers.Text;

namespace Relp;

/// <summary>Incremental parser for octet-counted RELP frames.</summary>
public sealed class RelpParser
{
    private byte[] _buffer = Array.Empty<byte>();
    private int _count;

    /// <summary>Gets a RELP API value.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Gets a RELP API value.</summary>
    public int TransactionId { get; private set; }

    /// <summary>Gets a RELP API value.</summary>
    public RelpCommand Command { get; private set; }

    /// <summary>Gets a RELP API value.</summary>
    public int Length { get; private set; }

    /// <summary>Provides a RELP API operation.</summary>
    public byte[] Data { get; private set; } = Array.Empty<byte>();

    /// <summary>Provides a RELP API operation.</summary>
    public byte[] RemainingBytes { get; private set; } = Array.Empty<byte>();

    /// <summary>Provides a RELP API operation.</summary>
    public void Parse(byte value)
    {
        Span<byte> single = stackalloc byte[1];
        single[0] = value;
        Parse(single);
    }

    /// <summary>Provides a RELP API operation.</summary>
    public void Parse(ReadOnlySpan<byte> bytes)
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("Parser has already completed a RELP frame. Create a new parser for additional frames and pass RemainingBytes first.");
        }

        EnsureCapacity(_count + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_count));
        _count += bytes.Length;

        TryParseBufferedFrame();
    }

    /// <summary>Provides a RELP API operation.</summary>
    public RelpFrameRx ToFrame()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException("Parser has not completed parsing a RELP frame.");
        }

        return new RelpFrameRx(TransactionId, Command, Length, Data);
    }

    private void TryParseBufferedFrame()
    {
        var span = _buffer.AsSpan(0, _count);
        var firstSpace = span.IndexOf((byte)' ');
        if (firstSpace < 0)
        {
            return;
        }

        var afterFirstSpace = firstSpace + 1;
        var secondSpaceOffset = span[afterFirstSpace..].IndexOf((byte)' ');
        if (secondSpaceOffset < 0)
        {
            return;
        }

        var secondSpace = afterFirstSpace + secondSpaceOffset;

        var afterSecondSpace = secondSpace + 1;
        var headerTail = span[afterSecondSpace..];
        var thirdSpaceOffset = headerTail.IndexOf((byte)' ');
        var newlineOffset = headerTail.IndexOf((byte)'\n');
        if (thirdSpaceOffset < 0 && newlineOffset < 0)
        {
            return;
        }

        var thirdSpace = thirdSpaceOffset < 0 ? -1 : afterSecondSpace + thirdSpaceOffset;
        var newline = newlineOffset < 0 ? -1 : afterSecondSpace + newlineOffset;
        if (newline >= 0 && (thirdSpace < 0 || newline < thirdSpace))
        {
            thirdSpace = -1;
        }

        if (!Utf8Parser.TryParse(span[..firstSpace], out int transactionId, out var consumed) ||
            consumed != firstSpace ||
            transactionId is < TxId.MinValue or > TxId.MaxValue)
        {
            throw new FormatException($"RELP transaction id must be between {TxId.MinValue} and {TxId.MaxValue}.");
        }

        if (!RelpCommandExtensions.TryParseProtocolSpan(span.Slice(afterFirstSpace, secondSpace - afterFirstSpace), out var command))
        {
            throw new FormatException("Invalid RELP command.");
        }

        var lengthEnd = thirdSpace < 0 ? newline : thirdSpace;
        if (!Utf8Parser.TryParse(span.Slice(afterSecondSpace, lengthEnd - afterSecondSpace), out int length, out consumed) ||
            consumed != lengthEnd - afterSecondSpace ||
            length < 0)
        {
            throw new FormatException("Negative or invalid payload length.");
        }

        if (thirdSpace < 0)
        {
            if (length != 0)
            {
                throw new FormatException("Non-empty RELP frame is missing the payload separator.");
            }

            Complete(transactionId, command, length, ReadOnlySpan<byte>.Empty, newline + 1);
            return;
        }

        var dataStart = thirdSpace + 1;
        var frameEnd = dataStart + length;
        if (_count <= frameEnd)
        {
            return;
        }

        if (_buffer[frameEnd] != (byte)'\n')
        {
            throw new FormatException("RELP frame is not terminated after the declared payload length.");
        }

        Complete(transactionId, command, length, span.Slice(dataStart, length), frameEnd + 1);
    }

    private void Complete(int transactionId, RelpCommand command, int length, ReadOnlySpan<byte> data, int remainderStart)
    {
        TransactionId = transactionId;
        Command = command;
        Length = length;
        Data = data.ToArray();
        RemainingBytes = _buffer.AsSpan(remainderStart, _count - remainderStart).ToArray();
        IsComplete = true;
    }

    private void EnsureCapacity(int needed)
    {
        if (_buffer.Length >= needed)
        {
            return;
        }

        var newSize = Math.Max(needed, _buffer.Length == 0 ? 256 : _buffer.Length * 2);
        Array.Resize(ref _buffer, newSize);
    }
}
