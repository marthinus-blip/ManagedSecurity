using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ManagedSecurity.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedSecurity.Test;

[TestClass]
public class ShieldSessionTests
{
    [TestMethod]
    public async Task Handshake_Success_DerivesSameKey()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var clientTask = Task.Run(async () =>
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var shield = new ShieldSession();
            return await shield.PerformHandshakeAsync(client.GetStream());
        });

        var serverTask = Task.Run(async () =>
        {
            using var server = await listener.AcceptTcpClientAsync();
            using var shield = new ShieldSession();
            return await shield.PerformHandshakeAsync(server.GetStream());
        });

        byte[] clientKey = await clientTask;
        byte[] serverKey = await serverTask;

        listener.Stop();

        CollectionAssert.AreEqual(clientKey, serverKey, "Keys must match after handshake.");
        Assert.AreEqual(32, clientKey.Length, "Key must be 256 bits.");
    }
}
