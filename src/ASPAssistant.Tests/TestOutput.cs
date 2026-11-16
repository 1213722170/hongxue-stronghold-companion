namespace ASPAssistant.Tests;

internal static class TestOutput
{
    public static string Directory { get; } = Path.GetFullPath(
        Environment.GetEnvironmentVariable("ASPASSISTANT_TEST_RESULTS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "TestResults"));
}
