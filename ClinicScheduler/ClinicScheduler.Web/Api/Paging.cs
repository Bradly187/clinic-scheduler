namespace ClinicScheduler.Web.Api;

/// <summary>
/// Normalizes optional page/pageSize query parameters for list endpoints.
/// When neither parameter is supplied, endpoints keep their legacy unpaged behavior.
/// </summary>
public static class Paging
{
    /// <summary>Response header carrying the total (unpaged) row count.</summary>
    public const string TotalCountHeader = "X-Total-Count";

    /// <summary>Page size used when only a page number is supplied.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Upper bound on the page size a client may request.</summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// Returns skip/take when paging was requested, or null when the caller wants the full list.
    /// </summary>
    public static (int Skip, int Take)? Normalize(int? page, int? pageSize)
    {
        if (page is null && pageSize is null) return null;

        var pageNumber = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        return ((pageNumber - 1) * size, size);
    }

    /// <summary>
    /// Normalizes a query-string DateTime to UTC for PostgreSQL timestamptz comparisons.
    /// Unspecified kinds are treated as UTC.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
