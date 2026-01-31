using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Common;

namespace ManagedSecurity.Test;

[TestClass]
public class UnitTest1
{
    [TestMethod]
    public void VerifyHeaderAotCompliance()
    {
        // Exercise some code from the library
        var header = new Bindings.Header(new byte[100]);
        Assert.IsNotNull(header);
    }
}