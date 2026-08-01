using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Middleware;

namespace UnitTest.Middleware;

public class AuditHashChainTests
{
    public AuditHashChainTests()
    {
        AuditHashChain.Reset();
    }

    private static AuditLogEntry AppendEntry(string method = "GET", string path = "/api/v1/test", int status = 200)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            HttpMethod = method,
            Path = path,
            StatusCode = status,
            ResponseTimeMs = 12,
            User = "testuser",
            UserAgent = "test-agent"
        };
        var content = AuditHashChain.BuildContent(entry);
        (entry.PrevHash, entry.Hash) = AuditHashChain.Append(content);
        return entry;
    }

    private static bool IsHex64(string hash)
    {
        return hash.Length == 64 && hash.All(Uri.IsHexDigit);
    }

    [Fact]
    public void Append_Chains_Hashes_Sequentially()
    {
        var first = AppendEntry(method: "GET", path: "/a");
        var second = AppendEntry(method: "POST", path: "/b");
        var third = AppendEntry(method: "DELETE", path: "/c");

        second.PrevHash.Should().Be(first.Hash);
        third.PrevHash.Should().Be(second.Hash);
        first.PrevHash.Should().BeNull();

        IsHex64(first.Hash!).Should().BeTrue();
        IsHex64(second.Hash!).Should().BeTrue();
        IsHex64(third.Hash!).Should().BeTrue();
        new[] { first.Hash, second.Hash, third.Hash }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Append_Same_Content_Produces_Different_Hashes_When_Chained()
    {
        var first = AppendEntry(path: "/same");
        var second = AppendEntry(path: "/same");

        first.Hash.Should().NotBe(second.Hash, "el hash del eslabon incluye el hash previo");
    }

    [Fact]
    public void VerifyChain_ReturnsTrue_ForIntactChain()
    {
        var entries = new List<AuditLogEntry>
        {
            AppendEntry(path: "/a"),
            AppendEntry(path: "/b"),
            AppendEntry(path: "/c")
        };

        AuditHashChain.VerifyChain(entries).Should().BeTrue();
    }

    [Fact]
    public void VerifyChain_Detects_Tampered_Entry()
    {
        var entries = new List<AuditLogEntry>
        {
            AppendEntry(path: "/a"),
            AppendEntry(path: "/b"),
            AppendEntry(path: "/c")
        };

        entries[1].StatusCode = 500;

        AuditHashChain.VerifyChain(entries).Should().BeFalse();
    }

    [Fact]
    public void VerifyChain_Detects_Missing_Entry()
    {
        var entries = new List<AuditLogEntry>
        {
            AppendEntry(path: "/a"),
            AppendEntry(path: "/b"),
            AppendEntry(path: "/c")
        };

        entries.RemoveAt(1);

        AuditHashChain.VerifyChain(entries).Should().BeFalse();
    }

    [Fact]
    public void VerifyChain_Detects_Reordered_Entries()
    {
        var entries = new List<AuditLogEntry>
        {
            AppendEntry(path: "/a"),
            AppendEntry(path: "/b"),
            AppendEntry(path: "/c")
        };

        (entries[0], entries[2]) = (entries[2], entries[0]);

        AuditHashChain.VerifyChain(entries).Should().BeFalse();
    }

    [Fact]
    public void VerifyChain_Detects_Altered_PrevHash()
    {
        var entries = new List<AuditLogEntry>
        {
            AppendEntry(path: "/a"),
            AppendEntry(path: "/b"),
            AppendEntry(path: "/c")
        };

        entries[1].PrevHash = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

        AuditHashChain.VerifyChain(entries).Should().BeFalse();
    }

    [Fact]
    public void VerifyChain_Detects_First_Entry_With_PrevHash()
    {
        var entries = new List<AuditLogEntry>
        {
            AppendEntry(path: "/a"),
            AppendEntry(path: "/b")
        };

        entries[0].PrevHash = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

        AuditHashChain.VerifyChain(entries).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Logs_Audit_Entry_With_Chain_Hashes()
    {
        var logger = new Mock<ILogger<AuditLoggingMiddleware>>();
        var middleware = new AuditLoggingMiddleware(
            _ => Task.CompletedTask,
            logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/test";
        context.Response.StatusCode = 200;
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }));

        await middleware.InvokeAsync(context);

        LogVerifier.VerifyLog(logger, LogLevel.Information, "Hash=", Times.Once());
        LogVerifier.VerifyLog(logger, LogLevel.Information, "PrevHash=", Times.Once());
    }
}
