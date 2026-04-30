internal static class BenchmarkFixture
{
    internal static string OptionalPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    internal static string RequireOptionalLargeFixture(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Optional large benchmark fixture is missing. Add the file under tests/XLSight.Tests/TestData before running this benchmark.",
                path);
        }

        return path;
    }
}
