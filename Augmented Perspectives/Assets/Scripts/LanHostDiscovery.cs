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
    private readonly Queue<IPAddress> directTargets = new();
    private double nextDirectBatch, nextDirectRound;
    private int queriesSent, requestsReceived, repliesReceived;
    private bool sawBusyHost;
    public bool IsSearching { get; private set; }
    public event Action<IPEndPoint> HostFound;
    public event Action<string> Failed;
    public event Action<string> Diagnostic;

    public void StartHosting(ushort listenPort, ushort gameplayPort)
    {
        Stop();
        socket = Open(listenPort);
        gamePort = gameplayPort;
        sessionId = Guid.NewGuid().ToString("N");
        hosting = true;
        Diagnostic?.Invoke($"Host discovery listening on UDP {listenPort}; game port {gameplayPort}.");
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
        nextDirectBatch = nextDirectRound = now;
        queriesSent = requestsReceived = repliesReceived = 0;
        Diagnostic?.Invoke("Searching with broadcasts and direct local-subnet discovery requests.");
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
                try { socket.Send(query, query.Length, new IPEndPoint(address, discoveryPort)); sends++; queriesSent++; }
                catch (SocketException) { /* Another interface/broadcast may still work. */ }
            }
            if (sends == 0) Diagnostic?.Invoke("Broadcast send unavailable; continuing direct discovery.");
        }
        if (IsSearching && directTargets.Count == 0 && now >= nextDirectRound)
        {
            foreach (IPAddress target in DirectAddresses()) directTargets.Enqueue(target);
            nextDirectRound = now + 5.0;
            Diagnostic?.Invoke($"Direct discovery round: {directTargets.Count} candidate addresses.");
        }
        if (IsSearching && now >= nextDirectBatch)
        {
            nextDirectBatch = now + 0.03;
            byte[] query = Encoding.ASCII.GetBytes($"{Protocol}|FIND|{nonce}");
            for (int i = 0; i < 8 && directTargets.Count > 0; i++)
            {
                IPAddress address = directTargets.Dequeue();
                try { socket.Send(query, query.Length, new IPEndPoint(address, discoveryPort)); queriesSent++; }
                catch (SocketException) { /* Most candidate addresses have no game host. */ }
            }
        }
        // Limit work per frame and never wait for a packet on Unity's main thread.
        for (int i = 0; i < 32 && socket != null && HasPacket(); i++)
        {
            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            byte[] packet;
            try { packet = socket.Receive(ref sender); }
            catch (SocketException exception) when (TransientReceiveError(exception.SocketErrorCode)) { break; }
            if (packet.Length > 256 || sender.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            string[] fields = Encoding.ASCII.GetString(packet).Split('|');
            if (fields.Length < 3 || fields[0] != Protocol) continue;
            if (hosting && fields.Length == 3 && fields[1] == "FIND" &&
                Guid.TryParseExact(fields[2], "N", out _))
            {
                requestsReceived++;
                if (requestsReceived == 1) Diagnostic?.Invoke($"First discovery request received from {sender}.");
                string kind = hostHasSpace ? "HOST" : "BUSY";
                byte[] reply = Encoding.ASCII.GetBytes($"{Protocol}|{kind}|{fields[2]}|{sessionId}|{gamePort}");
                try { socket.Send(reply, reply.Length, sender); }
                catch (SocketException) { /* The querying device may have gone away. */ }
            }
            else if (IsSearching && fields.Length == 5 && fields[1] == "BUSY" && fields[2] == nonce)
            {
                if (!sawBusyHost) Diagnostic?.Invoke($"Host at {sender.Address} is reachable but its lobby is full.");
                sawBusyHost = true;
            }
            else if (IsSearching && fields.Length == 5 && fields[1] == "HOST" && fields[2] == nonce &&
                Guid.TryParseExact(fields[3], "N", out _) && ushort.TryParse(fields[4], out ushort port) && port > 0)
            {
                // Trust the packet's actual source address, not an advertised IP.
                repliesReceived++;
                if (!candidates.ContainsKey(fields[3])) Diagnostic?.Invoke($"Host reply received from {sender.Address}:{port}.");
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
        {
            Diagnostic?.Invoke($"Discovery timed out: {queriesSent} queries sent, {repliesReceived} host replies received.");
            Fail(sawBusyHost ? "Host found, but its lobby is full. Disconnect / Cancel on both headsets, then Host and Join again." :
                "No available host found. Use the same APK and Wi-Fi on both headsets. The network may be blocking communication between devices.");
        }
    }

    private bool HasPacket()
    {
        try { return socket.Available > 0; }
        catch (SocketException exception) when (TransientReceiveError(exception.SocketErrorCode)) { return false; }
    }

    private static bool TransientReceiveError(SocketError error) => error == SocketError.WouldBlock ||
        error == SocketError.ConnectionReset || error == SocketError.ConnectionRefused ||
        error == SocketError.HostUnreachable || error == SocketError.NetworkUnreachable;

    private static IEnumerable<IPAddress> DirectAddresses()
    {
        var addresses = new HashSet<string>();
        void AddRange(IPAddress address, IPAddress netmask)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any)) return;
            byte[] ip = address.GetAddressBytes();
            byte[] mask = netmask != null ? netmask.GetAddressBytes() : null;
            // Bound discovery traffic to at most 254 neighbours per interface.
            // If the subnet is larger, try the local /24; broadcasts still cover the subnet.
            if (mask == null || mask.Length != 4 || mask[0] != 255 || mask[1] != 255 || mask[2] != 255)
                mask = new byte[] { 255, 255, 255, 0 };
            byte network = (byte)(ip[3] & mask[3]);
            byte broadcast = (byte)(network | (byte)~mask[3]);
            for (int host = network + 1; host < broadcast; host++)
            {
                if (host == ip[3]) continue;
                addresses.Add(new IPAddress(new byte[] { ip[0], ip[1], ip[2], (byte)host }).ToString());
            }
        }
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation entry in adapter.GetIPProperties().UnicastAddresses)
                {
                    IPAddress mask = null;
                    try { mask = entry.IPv4Mask; } catch (Exception) { }
                    AddRange(entry.Address, mask);
                }
            }
        }
        catch (Exception) { }
#if UNITY_ANDROID && !UNITY_EDITOR
        // Read Android LinkProperties when managed interface enumeration is incomplete.
        try
        {
            using (var player = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity"))
            using (var connectivity = activity.Call<UnityEngine.AndroidJavaObject>("getSystemService", "connectivity"))
            using (var network = connectivity.Call<UnityEngine.AndroidJavaObject>("getActiveNetwork"))
            using (var properties = connectivity.Call<UnityEngine.AndroidJavaObject>("getLinkProperties", network))
            using (var links = properties.Call<UnityEngine.AndroidJavaObject>("getLinkAddresses"))
            {
                for (int i = 0; i < links.Call<int>("size"); i++)
                {
                    using (var link = links.Call<UnityEngine.AndroidJavaObject>("get", i))
                    using (var inet = link.Call<UnityEngine.AndroidJavaObject>("getAddress"))
                    {
                        if (!IPAddress.TryParse(inet.Call<string>("getHostAddress"), out IPAddress address) ||
                            address.AddressFamily != AddressFamily.InterNetwork) continue;
                        int prefix = link.Call<int>("getPrefixLength");
                        byte[] mask = new byte[4];
                        for (int b = 0; b < 4; b++, prefix -= 8)
                            mask[b] = prefix >= 8 ? (byte)255 : prefix <= 0 ? (byte)0 : (byte)(255 << (8 - prefix));
                        AddRange(address, new IPAddress(mask));
                    }
                }
            }
        }
        catch (Exception) { }
        // Compatibility fallback for older Quest firmware only if no address was found.
        if (addresses.Count == 0)
        {
        try
        {
            using (var player = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity"))
            using (var wifi = activity.Call<UnityEngine.AndroidJavaObject>("getSystemService", "wifi"))
            using (var dhcp = wifi.Call<UnityEngine.AndroidJavaObject>("getDhcpInfo"))
            {
                int ip = dhcp.Get<int>("ipAddress");
                int mask = dhcp.Get<int>("netmask");
                byte[] Bytes(int value) => new byte[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) };
                if (ip != 0) AddRange(new IPAddress(Bytes(ip)), mask != 0 ? new IPAddress(Bytes(mask)) : null);
            }
        }
        catch (Exception) { }
        }
#endif
        foreach (string address in addresses) yield return IPAddress.Parse(address);
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
        directTargets.Clear();
        sawBusyHost = false;
        requestsReceived = repliesReceived = queriesSent = 0;
    }

    public void Dispose() => Stop();
}