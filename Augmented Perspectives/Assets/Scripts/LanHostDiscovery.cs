using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

// Main-thread, non-blocking UDP discovery. Gameplay still uses UnityTransport.
public sealed class LanHostDiscovery : IDisposable
{
    private const string Protocol = "AP_BUILDING_GAME_LAN_1";
    private UdpClient socket;
    private bool hosting;
    private ushort gamePort, discoveryPort;
    private string nonce, sessionId;
    private double deadline, nextQuery, firstReply;
    private readonly Dictionary<string, IPEndPoint> candidates = new();
    public bool IsSearching { get; private set; }
    public event Action<IPEndPoint> HostFound;
    public event Action<string> Failed;

    public void StartHosting(ushort listenPort, ushort gameplayPort)
    {
        Stop();
        socket = Open(listenPort);
        gamePort = gameplayPort;
        sessionId = Guid.NewGuid().ToString("N");
        hosting = true;
    }

    public void StartSearching(ushort listenPort, double now, double timeout)
    {
        Stop();
        socket = Open(0); // An ephemeral reply port avoids conflicts with a local host.
        discoveryPort = listenPort;
        nonce = Guid.NewGuid().ToString("N");
        deadline = now + timeout;
        nextQuery = now;
        firstReply = -1;
        IsSearching = true;
    }

    private static UdpClient Open(int port)
    {
        var client = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            client.EnableBroadcast = true;
            client.Client.Blocking = false;
            client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    public void Tick(double now, bool hostHasSpace)
    {
        if (socket == null) return;
        if (IsSearching && now >= nextQuery)
        {
            nextQuery = now + 1.0;
            byte[] query = Encoding.ASCII.GetBytes($"{Protocol}|FIND|{nonce}");
            int sends = 0;
            foreach (IPAddress address in BroadcastAddresses())
            {
                try { socket.Send(query, query.Length, new IPEndPoint(address, discoveryPort)); sends++; }
                catch (SocketException) { /* Another interface/broadcast may still work. */ }
            }
            if (sends == 0)
            {
                Fail("Could not search the Wi-Fi network. Check the headset's Wi-Fi connection, then press Join again.");
                return;
            }
        }
        // Limit work per frame and never wait for a packet on Unity's main thread.
        for (int i = 0; i < 32 && socket != null && socket.Available > 0; i++)
        {
            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            byte[] packet;
            try { packet = socket.Receive(ref sender); }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.WouldBlock) { break; }
            if (packet.Length > 256 || sender.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            string[] fields = Encoding.ASCII.GetString(packet).Split('|');
            if (fields.Length < 3 || fields[0] != Protocol) continue;
            if (hosting && hostHasSpace && fields.Length == 3 && fields[1] == "FIND" &&
                Guid.TryParseExact(fields[2], "N", out _))
            {
                byte[] reply = Encoding.ASCII.GetBytes($"{Protocol}|HOST|{fields[2]}|{sessionId}|{gamePort}");
                try { socket.Send(reply, reply.Length, sender); }
                catch (SocketException) { /* The querying device may have gone away. */ }
            }
            else if (IsSearching && fields.Length == 5 && fields[1] == "HOST" && fields[2] == nonce &&
                Guid.TryParseExact(fields[3], "N", out _) && ushort.TryParse(fields[4], out ushort port) && port > 0)
            {
                // Trust the packet's actual source address, not an advertised IP.
                candidates[fields[3]] = new IPEndPoint(sender.Address, port);
                if (firstReply < 0) firstReply = now;
            }
        }
        if (!IsSearching) return;
        if (candidates.Count > 1)
        {
            Fail("Multiple hosts found. Keep only your intended host running, then press Join again.");
            return;
        }
        if (candidates.Count == 1 && now - firstReply >= 0.8)
        {
            IPEndPoint endpoint = null;
            foreach (IPEndPoint candidate in candidates.Values) endpoint = candidate;
            Stop();
            HostFound?.Invoke(endpoint);
        }
        else if (now >= deadline)
            Fail("No available host found. Press Host on the other headset and check that both use the same Wi-Fi. Then press Join again.");
    }

    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        var addresses = new HashSet<string> { IPAddress.Broadcast.ToString() };
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation entry in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (entry.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(entry.Address)) continue;
                    try
                    {
                        byte[] ip = entry.Address.GetAddressBytes();
                        byte[] mask = entry.IPv4Mask.GetAddressBytes();
                        if (mask.Length != 4) continue;
                        byte[] broadcast = new byte[4];
                        for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                        addresses.Add(new IPAddress(broadcast).ToString());
                    }
                    catch (Exception) { /* Limited broadcast remains available on unsupported platforms. */ }
                }
            }
        }
        catch (Exception) { }
        foreach (string address in addresses) yield return IPAddress.Parse(address);
    }

    private void Fail(string message)
    {
        Stop();
        Failed?.Invoke(message);
    }

    public void Stop()
    {
        hosting = false;
        IsSearching = false;
        socket?.Dispose();
        socket = null;
        candidates.Clear();
    }

    public void Dispose() => Stop();
}