using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ManagedSecurity.Test;

[TestClass]
public class UnitTest1
{
    [TestMethod]
    public void DataProtection_AotCompliance_Demo()
    {
        // 1. Setup Dependency Injection
        var serviceCollection = new ServiceCollection();
        
        // Add Data Protection
        serviceCollection.AddDataProtection();

        // New Header Format Tests
        var headerTests = new HeaderTests();
        headerTests.Parse_BaseCase_Success();
        headerTests.Parse_InvalidMagic_Throws();
        headerTests.Parse_MacLength_Switch();
        headerTests.Parse_VariableLengthL_1ByteExtension();
        headerTests.Parse_VariableLengthKI_2ByteExtension();

        var services = serviceCollection.BuildServiceProvider();

        // 2. Encrypt/Decrypt Check
        var protectorProvider = services.GetRequiredService<IDataProtectionProvider>();
        var protector = protectorProvider.CreateProtector("ManagedSecurity.AotDemo");

        string originalData = "AOT Mystery Package";
        string protectedData = protector.Protect(originalData);
        string unprotectedData = protector.Unprotect(protectedData);

        Assert.AreEqual(originalData, unprotectedData);
        Console.WriteLine($"[AOT Demo] Successfully protected and unprotected: {unprotectedData}");
    }

    [TestMethod]
    public void PlatformSwitching_Demo()
    {
        // Demonstrating how Host creation would look with platform switching
        var builder = Host.CreateApplicationBuilder();

        #if LINUX
            Console.WriteLine("[AOT Demo] Compile-time check: LINUX detected. Adding Systemd support.");
            // In a real Host context: builder.Services.AddSystemd();
        #elif WINDOWS
            Console.WriteLine("[AOT Demo] Compile-time check: WINDOWS detected. Adding Windows Service support.");
            // In a real Host context: builder.Services.AddWindowsService();
        #else
            Console.WriteLine("[AOT Demo] Compile-time check: Unknown Platform.");
        #endif

        // Verify the logic branch at runtime matches the compile-time expectation
        #if LINUX
            Assert.IsTrue(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux));
        #endif
    }
}