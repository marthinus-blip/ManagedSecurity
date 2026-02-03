using BenchmarkDotNet.Running;
using ManagedSecurity.Benchmarks;

namespace ManagedSecurity.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
