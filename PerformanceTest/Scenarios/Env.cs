namespace PerformanceTest.Scenarios
{
    internal static class Env
    {
        public static int Int(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    }
}
