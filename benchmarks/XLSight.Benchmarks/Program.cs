using BenchmarkDotNet.Running;
using System.Reflection;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(GetBenchmarkArgs(args));

static string[] GetBenchmarkArgs(string[] args)
{
    if (args.Length > 0)
    {
        return args;
    }

    string[] defaultFilters = [.. typeof(Program).Assembly
        .GetTypes()
        .Where(static type => type.IsClass && type.IsPublic)
        .SelectMany(static type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        .Where(static method => method.GetCustomAttribute<BenchmarkDotNet.Attributes.BenchmarkAttribute>() is not null)
        .Where(static method => !method.Name.Contains("XlLarge", StringComparison.Ordinal))
        .Select(static method => $"{method.DeclaringType!.Name}.{method.Name}")];

    return ["--filter", .. defaultFilters];
}
