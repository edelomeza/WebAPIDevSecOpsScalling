using Microsoft.Extensions.Logging;
using Moq;

namespace UnitTest.Common
{
    public static class LogVerifier
    {
        public static void VerifyLog<T>(Mock<ILogger<T>> logger, LogLevel level, string contains, Times times)
        {
            logger.Verify(l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, t) => state.ToString()!.Contains(contains)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                times);
        }
    }
}
