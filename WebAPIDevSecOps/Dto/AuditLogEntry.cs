namespace WebAPIDevSecOps.Dto;

public class AuditLogEntry
{
    public string Timestamp { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long ResponseTimeMs { get; set; }
    public string? User { get; set; }
    public string? UserAgent { get; set; }
    public string? PrevHash { get; set; }
    public string? Hash { get; set; }
}
