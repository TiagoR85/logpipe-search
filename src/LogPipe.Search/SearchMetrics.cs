using System.Diagnostics.Metrics;

namespace LogPipe.Search;

public static class SearchMetrics
{
    public static readonly Meter Meter = new("LogPipe.Search");
    public static readonly Counter<long> Indexed =
        Meter.CreateCounter<long>("logpipe_search_entries_indexed_total");
    public static readonly Counter<long> Rejected =
        Meter.CreateCounter<long>("logpipe_search_entries_rejected_total");
    public static readonly Counter<long> BulkFailures =
        Meter.CreateCounter<long>("logpipe_search_bulk_failures_total");
}
