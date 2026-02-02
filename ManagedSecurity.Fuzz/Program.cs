using System;
using System.Diagnostics;
using System.Security.Cryptography;
using ManagedSecurity.Common;
using ManagedSecurity.Core;

namespace ManagedSecurity.Fuzz;

public class Program
{
    private static long _iterations = 0;
    private static long _exceptionsCaught = 0;
    private static long _hardCrashes = 0;

    public static void Main(string[] args)
    {
        Console.WriteLine("=========================================");
        Console.WriteLine("ManagedSecurity - Resiliency Fuzzer");
        Console.WriteLine("=========================================");
        
        long maxIterations = -1;
        if (args.Length >= 2 && args[0] == "--count") {
             long.TryParse(args[1], out maxIterations);
        } else if (args.Length >= 1 && args[0] == "--once") {
             maxIterations = 1;
        }

        var keyProvider = new MockKeyProvider();
        var cipher = new Cipher(keyProvider);

        Stopwatch sw = Stopwatch.StartNew();
        
        try 
        {
            while (maxIterations < 0 || _iterations < maxIterations)
            {
                RunFuzzCycle(cipher);
                _iterations++;

                if (_iterations % 10000 == 0)
                {
                    Console.WriteLine($"[{sw.Elapsed:hh\\:mm\\:ss}] Iterations: {_iterations:N0} | Expected Exc: {_exceptionsCaught:N0}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FATAL] Fuzzer Loop Panic: {ex.GetType().Name} - {ex.Message}");
            _hardCrashes++;
        }

        Console.WriteLine("\nFuzzing Summary:");
        Console.WriteLine($"Total Iterations: {_iterations}");
        Console.WriteLine($"Validation Failures (Expected): {_exceptionsCaught}");
        Console.WriteLine($"Hard Crashes (Unexpected): {_hardCrashes}");
    }

    private static void RunFuzzCycle(Cipher cipher)
    {
        // Strategy 1: Random Garbage Parsing
        FuzzHeaderParsing();

        // Strategy 2: Mutated Valid Message Decryption
        FuzzMutatedDecryption(cipher);
    }

    private static void FuzzHeaderParsing()
    {
        int length = RandomNumberGenerator.GetInt32(0, 2048);
        byte[] data = new byte[length];
        RandomNumberGenerator.Fill(data);

        try
        {
            var header = new Bindings.Header(data);
            // Verify offsets are within range of the data used to construct it
            _ = header.GetIv(data);
            _ = header.GetMac(data);
            _ = header.GetPayload(data);
        }
        catch (ArgumentException) { _exceptionsCaught++; }
        catch (InvalidOperationException) { _exceptionsCaught++; }
        catch (NotImplementedException) { _exceptionsCaught++; }
        catch (NotSupportedException) { _exceptionsCaught++; }
        catch (Exception ex)
        {
            ReportHardCrash("HeaderParsing", data, ex);
        }
    }

    private static void FuzzMutatedDecryption(Cipher cipher)
    {
        byte[] plain = new byte[RandomNumberGenerator.GetInt32(0, 512)];
        RandomNumberGenerator.Fill(plain);
        byte[] msg = cipher.Encrypt(plain, RandomNumberGenerator.GetInt32(0, 4096));

        // Mutate
        int mutationCount = RandomNumberGenerator.GetInt32(1, 10);
        for (int i = 0; i < mutationCount; i++)
        {
            msg[RandomNumberGenerator.GetInt32(0, msg.Length)] = (byte)RandomNumberGenerator.GetInt32(0, 256);
        }

        try
        {
            cipher.Decrypt(msg);
        }
        catch (ArgumentException) { _exceptionsCaught++; }
        catch (InvalidOperationException) { _exceptionsCaught++; }
        catch (NotImplementedException) { _exceptionsCaught++; }
        catch (NotSupportedException) { _exceptionsCaught++; }
        catch (CryptographicException) { _exceptionsCaught++; }
        catch (Exception ex)
        {
            ReportHardCrash("MutatedDecryption", msg, ex);
        }
    }

    private static void ReportHardCrash(string phase, byte[] data, Exception ex)
    {
        Console.WriteLine($"\n[!!!] UNEXPECTED CRASH in {phase}");
        Console.WriteLine($"Type: {ex.GetType().FullName}");
        Console.WriteLine($"Message: {ex.Message}");
        Console.WriteLine($"Stack:\n{ex.StackTrace}");
        Console.WriteLine($"Hex Payload (Truncated): {BitConverter.ToString(data, 0, Math.Min(data.Length, 64)).Replace("-", "")}...");
        
        _hardCrashes++;
        
        // Critical failures should stop the fuzzer
        if (ex is AccessViolationException || ex is NullReferenceException || ex is IndexOutOfRangeException)
        {
             Console.WriteLine("\nHalting fuzzer due to critical memory safety / bounds bug.");
             Environment.Exit(1);
        }
    }

    private class MockKeyProvider : IKeyProvider
    {
        private readonly byte[] _key = new byte[32];
        public ReadOnlyMemory<byte> GetKey(int keyIndex) => _key;
    }
}
