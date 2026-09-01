using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobFlow.Core;

namespace JobFlow.SqlServer;

internal static class JobSearchCursorCodec
{
    private const int CurrentVersion = 1;
    private const int MaximumCursorLength = 1024;

    public static string Encode(DateTimeOffset createdAt, Guid id, JobSearchCriteria criteria)
    {
        var cursor = new StoredCursor(
            CurrentVersion,
            createdAt,
            id,
            CalculateFilterFingerprint(criteria));

        return ToBase64Url(JsonSerializer.SerializeToUtf8Bytes(cursor));
    }

    public static JobSearchPosition Decode(string value, JobSearchCriteria criteria)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumCursorLength)
        {
            throw new ArgumentException("The search cursor is invalid.", nameof(value));
        }

        try
        {
            var cursor = JsonSerializer.Deserialize<StoredCursor>(FromBase64Url(value))
                ?? throw new ArgumentException("The search cursor is invalid.", nameof(value));

            if (cursor.Version != CurrentVersion
                || cursor.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(cursor.FilterFingerprint))
            {
                throw new ArgumentException("The search cursor is invalid.", nameof(value));
            }

            if (!string.Equals(
                    cursor.FilterFingerprint,
                    CalculateFilterFingerprint(criteria),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("The search cursor does not match the supplied filters.", nameof(value));
            }

            return new JobSearchPosition(cursor.CreatedAt, cursor.Id);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The search cursor is invalid.", nameof(value), exception);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The search cursor is invalid.", nameof(value), exception);
        }
    }

    private static string CalculateFilterFingerprint(JobSearchCriteria criteria)
    {
        var filters = new CursorFilters(
            criteria.Status,
            criteria.JobType,
            criteria.WorkerId,
            criteria.CreatedFrom?.ToUniversalTime(),
            criteria.CreatedTo?.ToUniversalTime());

        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(filters)));
    }

    private static string ToBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => throw new FormatException("The search cursor is invalid.")
        };

        return Convert.FromBase64String(base64);
    }

    private sealed record StoredCursor(
        int Version,
        DateTimeOffset CreatedAt,
        Guid Id,
        string FilterFingerprint);

    private sealed record CursorFilters(
        JobStatus? Status,
        string? JobType,
        string? WorkerId,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo);
}

internal readonly record struct JobSearchPosition(DateTimeOffset CreatedAt, Guid Id);
