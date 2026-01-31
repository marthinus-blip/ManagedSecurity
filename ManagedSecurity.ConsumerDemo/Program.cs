using System;
using ManagedSecurity.Common;

Console.WriteLine("ManagedSecurity Consumer Demo");
Console.WriteLine("-----------------------------");

try
{
    // Using the library from the local NuGet package
    var header = new Bindings.Header(new byte[100]);
    Console.WriteLine("Successfully initialized Bindings.Header from the NuGet package!");
    Console.WriteLine("Package Version: 0.0.1");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
