using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal enum WorkerMessageType
{
    Ready,
    BeforeNetwork,
    ContinueNetwork,
    FinalResult
}

internal sealed class WorkerMessage
{
    public string SessionId { get; set; } = "";
    public string Nonce { get; set; } = "";
    public WorkerMessageType Type { get; set; }
    public RemediationBatchResult? Result { get; set; }
}

internal static class WorkerProtocol
{
    internal static void ValidateMessage(
        WorkerMessage message,
        string expectedSession,
        string expectedNonce,
        WorkerMessageType expectedType)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSession);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);

        if (!string.Equals(message.SessionId, expectedSession, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Session ID сообщения worker не совпадает с текущей сессией.");
        if (!FixedTimeAsciiEquals(message.Nonce, expectedNonce))
            throw new InvalidDataException("Nonce сообщения worker не прошёл проверку.");
        if (message.Type != expectedType)
            throw new InvalidOperationException($"Нарушен порядок фаз worker: ожидалась {expectedType}, получена {message.Type}.");
    }

    internal static bool FixedTimeAsciiEquals(string? actual, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var actualBytes = Encoding.ASCII.GetBytes(actual ?? string.Empty);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}

internal sealed class JsonWorkerMessageChannel : IWorkerMessageChannel, IDisposable
{
    private const int MaxMessageChars = 1024 * 1024;
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private bool _disposed;

    internal JsonWorkerMessageChannel(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanWrite)
            throw new ArgumentException("Worker message stream must be readable and writable.", nameof(stream));

        _stream = stream;
        _leaveOpen = leaveOpen;
        _reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
    }

    public void Send(WorkerMessage message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);
        var json = JsonSerializer.Serialize(message, JsonOptions());
        if (json.Length > MaxMessageChars)
            throw new InvalidDataException("Worker protocol message exceeds the bounded size limit.");
        _writer.WriteLine(json);
    }

    public WorkerMessage Receive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var line = ReadBoundedLine();
        try
        {
            return JsonSerializer.Deserialize<WorkerMessage>(line, JsonOptions())
                ?? throw new InvalidDataException("Worker protocol message is empty or invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Worker protocol JSON is invalid.", ex);
        }
    }

    private string ReadBoundedLine()
    {
        var builder = new StringBuilder();
        while (true)
        {
            var value = _reader.Read();
            if (value < 0)
            {
                if (builder.Length == 0) throw new EndOfStreamException("Worker protocol stream closed before a message was received.");
                break;
            }
            if (value == '\n') break;
            builder.Append((char)value);
            if (builder.Length > MaxMessageChars)
                throw new InvalidDataException("Worker protocol message exceeds the bounded size limit.");
        }
        return builder.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Dispose();
        _reader.Dispose();
        if (!_leaveOpen) _stream.Dispose();
    }

    private static JsonSerializerOptions JsonOptions()
        => new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
}
