// ProgressStore.cs

using System.Collections.Concurrent;
using System.Text.Json;
using Shared;
using WBSL.Models;

public static class ProgressStore
{
    // хранит состояние по jobId
    private static readonly ConcurrentDictionary<Guid, FetchJobInfo> _jobs = new();

    public static Guid CreateJob(int totalItems)
    {
        var id = Guid.NewGuid();
        _jobs[id] = new FetchJobInfo {
            Total = totalItems,
            Processed = 0,
            Status = JobStatus.Running
        };
        return id;
    }

    public static FetchJobInfo? GetJob(Guid jobId)
        => _jobs.TryGetValue(jobId, out var info) ? info : null;

    public static void UpdateProgress(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var info))
            Interlocked.Increment(ref info.Processed);
    }

    public static void CompleteJob(Guid jobId, List<JsonElement> products, List<product_attribute> attrs)
    {
        if (_jobs.TryGetValue(jobId, out var info))
        {
            info.Status = JobStatus.Completed;
            info.Products = products;
            info.Attributes = attrs;
        }
    }
    public static void FailJob(Guid id)
    {
        if (_jobs.TryGetValue(id, out var j))
            j.Status = JobStatus.Failed;
    }
    
    public static void CompleteJob(Guid id, WbApiResult result)
    {
        if (_jobs.TryGetValue(id, out var j))
        {
            j.Processed = j.Total;
            j.Status    = JobStatus.Completed;
            j.Result    = result;
        }
    }

    public class FetchJobInfo
    {
        public int Total;
        public int Processed;
        public JobStatus Status;
        public List<JsonElement>? Products;
        public List<product_attribute>? Attributes;
        public WbApiResult? Result { get; set; }
    }

    public enum JobStatus { Running, Completed, Failed }
}