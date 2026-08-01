using System.Security.Cryptography;
using System.Text;
using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Middleware
{
    public static class AuditHashChain
    {
        private static readonly object Lock = new();
        private static string? _lastHash;

        public static string BuildContent(AuditLogEntry entry)
        {
            return string.Join('|',
                entry.Timestamp,
                entry.HttpMethod,
                entry.Path,
                entry.StatusCode.ToString(),
                entry.ResponseTimeMs.ToString(),
                entry.User ?? string.Empty,
                entry.UserAgent ?? string.Empty);
        }

        public static string ComputeHash(string content)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public static (string? PrevHash, string Hash) Append(string content)
        {
            lock (Lock)
            {
                var prev = _lastHash;
                var hash = ComputeHash(prev == null ? content : prev + "|" + content);
                _lastHash = hash;
                return (prev, hash);
            }
        }

        public static bool VerifyChain(IReadOnlyList<AuditLogEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                if (i == 0)
                {
                    if (entry.PrevHash != null)
                        return false;
                }
                else if (entry.PrevHash != entries[i - 1].Hash)
                {
                    return false;
                }

                var content = BuildContent(entry);
                var expected = entry.PrevHash == null ? content : entry.PrevHash + "|" + content;
                if (!string.Equals(entry.Hash, ComputeHash(expected), StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        public static void Reset()
        {
            lock (Lock)
            {
                _lastHash = null;
            }
        }
    }
}
