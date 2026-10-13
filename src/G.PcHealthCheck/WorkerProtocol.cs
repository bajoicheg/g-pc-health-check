using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace G.PcHealthCheck;

internal enum WorkerActionNamespace
{
    ServiceDesk = 0,
    SecurityHardening = 1
}

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
    public WorkerActionNamespace Namespace { get; set; } = WorkerActionNamespace.ServiceDesk;
    public RemediationBatchResult? Result { get; set; }
    public SecurityHardeningBatchResult? SecurityResult { get; set; }
}

internal static class WorkerProtocol
{
    // Backward-compatible 0.16 Service Desk protocol surface. Existing reflection
    // tests intentionally resolve this exact method name and four-argument shape.
    internal static void ValidateMessage(
        WorkerMessage message,
        string expectedSession,
        string expectedNonce,
        WorkerMessageType expectedType)
        => ValidateNamespacedMessage(
            message,
            expectedSession,
            expectedNonce,
            expectedType,
            WorkerActionNamespace.ServiceDesk);

    internal static void ValidateNamespacedMessage(
        WorkerMessage message,
        string expectedSession,
        string expectedNonce,
        WorkerMessageType expectedType,
        WorkerActionNamespace expectedNamespace)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSession);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);
        if (!Enum.IsDefined(typeof(WorkerActionNamespace), expectedNamespace))
            throw new InvalidDataException("Worker action namespace is invalid.");

        if (!string.Equals(message.SessionId, expectedSession, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Session ID сообщения worker не совпадает с текущей сессией.");
        if (!FixedTimeAsciiEquals(message.Nonce, expectedNonce))
            throw new InvalidDataException("Nonce сообщения worker не прошёл проверку.");
        if (message.Type != expectedType)
            throw new InvalidOperationException($"Нарушен порядок фаз worker: ожидалась {expectedType}, получена {message.Type}.");
        if (!Enum.IsDefined(typeof(WorkerActionNamespace), message.Namespace) || message.Namespace != expectedNamespace)
            throw new InvalidDataException($"Worker namespace mismatch: expected {expectedNamespace}, received {message.Namespace}.");
    }

    internal static void ValidateActionIds(WorkerActionNamespace actionNamespace, IReadOnlyCollection<string> actionIds)
    {
        ArgumentNullException.ThrowIfNull(actionIds);
        if (!Enum.IsDefined(typeof(WorkerActionNamespace), actionNamespace))
            throw new InvalidDataException("Worker action namespace is invalid.");
        if (actionIds.Count == 0)
            throw new InvalidOperationException("Worker action list is empty.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in actionIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                throw new InvalidOperationException("Worker action list contains an empty or duplicate action ID.");

            var allowed = actionNamespace switch
            {
                WorkerActionNamespace.ServiceDesk => ServiceDeskActionRegistry.Find(id) is not null,
                WorkerActionNamespace.SecurityHardening => SecurityHardeningActionRegistry.Find(id) is not null,
                _ => false
            };
            if (!allowed)
                throw new InvalidOperationException($"Action ID is not allowed in {actionNamespace} namespace: {id}.");
        }
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

    public JsonWorkerMessageChannel(Stream stream, bool leaveOpen = false)
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
