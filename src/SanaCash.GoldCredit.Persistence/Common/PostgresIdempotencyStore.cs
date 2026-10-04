using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Persistence.Common;

public sealed class PostgresIdempotencyStore(PostgresSession session) : IIdempotencyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<TResponse>> ExecuteAsync<TResponse>(
        ClientId clientId,
        string operation,
        string key,
        string requestFingerprint,
        Func<CancellationToken, Task<Result<TResponse>>> execute,
        CancellationToken cancellationToken = default)
    {
        await using var claim = new NpgsqlCommand(
            "INSERT INTO platform.idempotency_records " +
            "(client_id, operation, idempotency_key, request_fingerprint) " +
            "VALUES (@client_id, @operation, @key, @fingerprint) " +
            "ON CONFLICT (client_id, operation, idempotency_key) DO NOTHING;",
            session.Connection,
            session.Transaction);
        claim.Parameters.AddWithValue("client_id", clientId.Value);
        claim.Parameters.AddWithValue("operation", operation);
        claim.Parameters.AddWithValue("key", key);
        claim.Parameters.AddWithValue("fingerprint", requestFingerprint);

        var isNew = await claim.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (!isNew)
        {
            return await ReplayAsync<TResponse>(clientId, operation, key, requestFingerprint, cancellationToken);
        }

        var result = await execute(cancellationToken);
        await using var complete = new NpgsqlCommand(
            "UPDATE platform.idempotency_records SET is_success = @is_success, response_json = @response_json, " +
            "error_code = @error_code, error_description = @error_description, completed_at = now() " +
            "WHERE client_id = @client_id AND operation = @operation AND idempotency_key = @key;",
            session.Connection,
            session.Transaction);
        complete.Parameters.AddWithValue("is_success", result.IsSuccess);
        var responseParameter = complete.Parameters.Add("response_json", NpgsqlDbType.Jsonb);
        responseParameter.Value = result.IsSuccess
            ? JsonSerializer.Serialize(result.Value, JsonOptions)
            : DBNull.Value;
        complete.Parameters.AddWithValue("error_code", result.IsFailure ? result.Error.Code : DBNull.Value);
        complete.Parameters.AddWithValue("error_description", result.IsFailure ? result.Error.Description : DBNull.Value);
        complete.Parameters.AddWithValue("client_id", clientId.Value);
        complete.Parameters.AddWithValue("operation", operation);
        complete.Parameters.AddWithValue("key", key);
        await complete.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    private async Task<Result<TResponse>> ReplayAsync<TResponse>(
        ClientId clientId,
        string operation,
        string key,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT request_fingerprint, is_success, response_json::text, error_code, error_description " +
            "FROM platform.idempotency_records " +
            "WHERE client_id = @client_id AND operation = @operation AND idempotency_key = @key;",
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("client_id", clientId.Value);
        command.Parameters.AddWithValue("operation", operation);
        command.Parameters.AddWithValue("key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<TResponse>.Failure(new Error("RequestInProgress", "The idempotent request is still being processed."));
        }

        if (!string.Equals(reader.GetString(0), requestFingerprint, StringComparison.Ordinal))
        {
            return Result<TResponse>.Failure(new Error("IdempotencyKeyReused", "The key was already used with a different request."));
        }

        if (reader.IsDBNull(1))
        {
            return Result<TResponse>.Failure(new Error("RequestInProgress", "The idempotent request is still being processed."));
        }

        if (!reader.GetBoolean(1))
        {
            return Result<TResponse>.Failure(new Error(reader.GetString(3), reader.GetString(4)));
        }

        var response = JsonSerializer.Deserialize<TResponse>(reader.GetString(2), JsonOptions);
        return response is null
            ? Result<TResponse>.Failure(new Error("IdempotencyResponseUnavailable", "The stored response could not be replayed."))
            : Result<TResponse>.Success(response);
    }
}