using System.Security.Cryptography;
using System.Text.Json;

namespace SanaCash.GoldCredit.Application.Behaviors.Idempotency;

internal static class RequestFingerprint
{
    public static string Create<TRequest>(string operation, TRequest request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Operation = operation, Request = request });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}