using System.Security.Cryptography;
using System.Text;

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
