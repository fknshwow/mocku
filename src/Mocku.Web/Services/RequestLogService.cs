using System.Collections.Concurrent;
using System.Text.Json;

namespace Mocku.Web.Services;

public class RequestLogService
{
    private readonly ConcurrentQueue<RequestLogEntry> _logs = new();
    private readonly int _maxLogEntries = 1000; // Keep last 1000 entries
    private readonly ILogger<RequestLogService> _logger;

    public RequestLogService(ILogger<RequestLogService> logger)
    {
        _logger = logger;
    }

    public event Action? LogsChanged;

    public void LogRequest(RequestLogEntry entry)
    {
        _logs.Enqueue(entry);
        
        // Keep only the most recent entries
        while (_logs.Count > _maxLogEntries)
        {
            _logs.TryDequeue(out _);
        }

        _logger.LogInformation("Request logged: {Method} {Path} -> {StatusCode}", 
            entry.Method, entry.Path, entry.StatusCode);
        
        LogsChanged?.Invoke();
    }

    public List<RequestLogEntry> GetLogs()
    {
        return _logs.ToList().OrderByDescending(x => x.Timestamp).ToList();
    }

    public void ClearLogs()
    {
        _logs.Clear();
        LogsChanged?.Invoke();
    }

    public int GetLogCount() => _logs.Count;
}

public class RequestLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string? QueryString { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? RequestBody { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public Dictionary<string, string> ResponseHeaders { get; set; } = new();
    public long ResponseTimeMs { get; set; }
    public string? MockFileName { get; set; }
    public bool IsMatchedByMock { get; set; }
    public string UserAgent { get; set; } = "";
    public string ClientIp { get; set; } = "";
}