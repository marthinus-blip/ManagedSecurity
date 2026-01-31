using System;
using ManagedSecurity.Common;

namespace ManagedSecurity.Test;

public class Program
{
    public static void Main()
    {
        Console.WriteLine("AOT Compliance Test Starting...");
        
        try 
        {
            // Exercise some code from the library
            var header = new Bindings.Header(new byte[100]);
            Console.WriteLine("Successfully created Header.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during AOT test: {ex.Message}");
            Environment.Exit(1);
        }

        Console.WriteLine("AOT Compliance Test Passed!");
    }
}