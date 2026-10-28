using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkLauncher : MonoBehaviour
{
    [Header("Local network connection")]
    [SerializeField] private ushort port = 7777;
    [SerializeField] private ushort discoveryPort = 47777;
    [SerializeField, Min(3f)] private float discoveryTimeout = 15f;
    [Header("Interface")]
    [SerializeField] private TMP_Text statusText;

    private NetworkManager manager;
    private LanHostDiscovery discovery;
    private StreamWriter logWriter;
    private bool starting, stopping, userStopped, startupFailed;
    private string startupError;

    private void Awake()
    {
        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "NetworkLogs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"network-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log");
            logWriter = new StreamWriter(path) { AutoFlush = true };
            Application.logMessageReceived += CaptureLog;
            Debug.Log($"[NetworkLauncher] Log file: {path}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NetworkLauncher] Could not open log file: {exception.Message}");
        }
    }

    private void Start()
    {
        Application.logMessageReceived -= CaptureLog;
        Application.logMessageReceived += CaptureLog;
        if (statusText != null)
        {
            statusText.rectTransform.sizeDelta = new Vector2(500f, 80f);
            statusText.rectTransform.anchoredPosition = new Vector2(0f, -70f);
            statusText.enableAutoSizing = true;
            statusText.fontSizeMin = 14f;
            statusText.fontSizeMax = 20f;
        }
        manager = NetworkManager.Singleton;
        if (manager == null) { SetStatus("ERROR: NetworkManager not found."); return; }
        discovery = new LanHostDiscovery();
        discovery.HostFound += ConnectDiscoveredHost;
        discovery.Failed += HandleDiscoveryFailure;
        manager.LogLevel = Unity.Netcode.LogLevel.Developer;
        manager.OnClientConnectedCallback += HandleClientConnected;
        manager.OnClientDisconnectCallback += HandleClientDisconnected;
        manager.OnTransportFailure += HandleTransportFailure;
        Debug.Log($"[NetworkLauncher] Local IPv4: {LocalAddresses()}; " +
                  $"automatic discovery UDP {discoveryPort}, game UDP {port}; Unity {Application.unityVersion}");
        SetStatus("Same Wi-Fi: choose Host on one headset, then Join on the other.");
    }

    private void OnDestroy()
    {
        discovery?.Dispose();
        Application.logMessageReceived -= CaptureLog;
        if (manager != null)
        {
            manager.OnClientConnectedCallback -= HandleClientConnected;
            manager.OnClientDisconnectCallback -= HandleClientDisconnected;
            manager.OnTransportFailure -= HandleTransportFailure;
        }
        logWriter?.Dispose();
        logWriter = null;
    }

    public void StartHost() => StartConnection(true);

    public void StartClient()
    {
        if (!CanStartOperation()) return;
        userStopped = startupFailed = false;
        startupError = null;
        try
        {
            discovery.StartSearching(discoveryPort, Time.unscaledTimeAsDouble, Mathf.Max(3f, discoveryTimeout));
            SetStatus($"Searching for a host on this Wi-Fi... ({discoveryTimeout:0}s)\nPress Host on the other headset. Cancel available below.");
        }
        catch (Exception exception)
        {
            discovery.Stop();
            Debug.LogException(exception);
            SetStatus($"ERROR: Wi-Fi host search could not start.\n{ShortMessage(exception.Message)}");
        }
    }

    private void ConnectDiscoveredHost(IPEndPoint endpoint)
    {
        if (userStopped || endpoint == null) return;
        Debug.Log($"[NetworkLauncher] Discovered available host: {endpoint}");
        StartConnection(false, endpoint);
    }

    private void HandleDiscoveryFailure(string message)
    {
        if (!userStopped) SetStatus(message);
    }

    private bool CanStartOperation()
    {
        if (manager == null || discovery == null)
        {
            SetStatus("ERROR: NetworkManager not ready.");
            return false;
        }
        if (stopping || starting || manager.ShutdownInProgress)
        {
            SetStatus("Please wait for the current connection operation.");
            return false;
        }
        if (discovery.IsSearching)
        {
            SetStatus("Searching for a host... Use Disconnect / Cancel to stop the search.");
            return false;
        }
        if (manager.IsListening || manager.IsClient || manager.IsServer)
        {
            Debug.Log("[NetworkLauncher] Ignored repeated start request: networking is already running.");
            return false;
        }
        return true;
    }

    private void StartConnection(bool asHost, IPEndPoint discoveredHost = null)
    {
        if (!CanStartOperation()) return;
        UnityTransport transport = manager.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null) { SetStatus("ERROR: Assign UnityTransport as Network Transport."); return; }
        if (!asHost && discoveredHost == null) { SetStatus("No host selected. Press Join to search again."); return; }
        if (asHost && port == discoveryPort) { SetStatus("ERROR: Discovery and game ports must be different."); return; }
        userStopped = false;
        startupError = null;
        startupFailed = false;
        starting = true;
        try
        {
            if (asHost) discovery.StartHosting(discoveryPort, port);
            // The host connects to itself locally, while listening on every interface.
            string address = asHost ? "127.0.0.1" : discoveredHost.Address.ToString();
            ushort targetPort = asHost ? port : (ushort)discoveredHost.Port;
            transport.SetConnectionData(address, targetPort, "0.0.0.0");
            Debug.Log($"[NetworkLauncher] Starting {(asHost ? "Host" : "Client")}: " +
                      $"address={address}:{targetPort}, localIPv4={LocalAddresses()}");
            SetStatus(asHost ? "Starting host..." : "Host found. Connecting...");
            bool result = asHost ? manager.StartHost() : manager.StartClient();
            if (!result)
            {
                startupFailed = true;
                string reason = startupError ?? "See the NetworkLogs file or adb logcat.";
                SetStatus($"ERROR: {(asHost ? "Host" : "Client")} failed to start.\n{ShortMessage(reason)}");
            }
            else if (asHost)
                SetStatus("Hosting on this Wi-Fi.\nOther player: press Join.\nWaiting for player...");
        }
        catch (Exception exception)
        {
            startupFailed = true;
            Debug.LogException(exception);
            SetStatus($"ERROR: Connection startup failed.\n{ShortMessage(exception.Message)}");
        }
        finally
        {
            starting = false;
            if (startupFailed) discovery.Stop();
        }
    }

    public void StopConnection()
    {
        discovery?.Stop();
        userStopped = true;
        startupFailed = false;
        startupError = null;
        CalibrationLobby.Instance?.CancelLocalCalibration();
        if (manager == null) manager = NetworkManager.Singleton;
        if (manager == null || (!manager.IsClient && !manager.IsServer && !manager.ShutdownInProgress))
        {
            stopping = false;
            SetStatus("Disconnected. Choose Host or Join.");
            return;
        }
        if (stopping) return;
        stopping = true;
        SetStatus("Disconnecting / cancelling connection...");
        if (!manager.ShutdownInProgress) manager.Shutdown();
    }

    private void Update()
    {
        if (discovery != null && manager != null)
        {
            try
            {
                if (manager.ShutdownInProgress) discovery.Stop();
                else discovery.Tick(Time.unscaledTimeAsDouble, manager.IsHost && manager.ConnectedClientsIds.Count < 2);
            }
            catch (Exception exception)
            {
                discovery.Stop();
                Debug.LogException(exception);
                if (!userStopped) SetStatus($"ERROR: Wi-Fi discovery stopped.\n{ShortMessage(exception.Message)}");
            }
        }
        if (!stopping) return;
        if (manager != null && (manager.ShutdownInProgress || manager.IsListening || manager.IsClient || manager.IsServer)) return;
        stopping = false;
        SetStatus("Disconnected. Choose Host or Join.");
    }

    private void HandleTransportFailure()
    {
        if (userStopped) return;
        discovery?.Stop();
        Debug.LogError("[NetworkLauncher] UnityTransport failed. See the preceding socket/initialization error.");
        if (!starting) SetStatus("ERROR: Transport failed. See NetworkLogs or adb logcat.");
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (userStopped)
        {
            if (!manager.ShutdownInProgress) manager.Shutdown();
            return;
        }
        Debug.Log($"[NetworkLauncher] Client connected: {clientId}; local={manager.LocalClientId}");
        if (manager.IsHost)
            SetStatus(clientId == manager.LocalClientId
                ? "Hosting on this Wi-Fi. Other player: press Join.\nWaiting for player..."
                : "Player connected!");
        else if (clientId == manager.LocalClientId) SetStatus("Connected to host!");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        string reason = manager != null ? manager.DisconnectReason : "";
        Debug.LogWarning($"[NetworkLauncher] Client disconnected: {clientId}; reason={reason}");
        if (userStopped || starting || startupFailed) return;
        if (manager == null || clientId == manager.LocalClientId)
        {
            discovery?.Stop();
            SetStatus("Disconnected from session." + (string.IsNullOrEmpty(reason) ? "" : $"\n{ShortMessage(reason)}"));
        }
        else if (manager.IsHost) SetStatus("Other player disconnected. Ready for another Join.");
    }

    private void CaptureLog(string message, string stackTrace, LogType type)
    {
        bool error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        if (starting && error && startupError == null)
            startupError = message;
        if (!error && type != LogType.Warning && !message.Contains("[NetworkLauncher]") &&
            !message.Contains("[NetworkGrab]") && !message.Contains("[CalibrationLobby]") && !message.Contains("[CalibrationDots]") && !message.Contains("[CalibrationInstructions]") && !message.Contains("Netcode") && !message.Contains("Transport"))
            return;
        try
        {
            logWriter?.WriteLine($"{DateTime.UtcNow:O} [{type}] {message}\n{stackTrace}");
        }
        catch (IOException)
        {
            // Never log from the log callback, which would recursively call this method.
        }
        catch (ObjectDisposedException) { }
    }

    private static string LocalAddresses()
    {
        var addresses = new List<string>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            foreach (var entry in adapter.GetIPProperties().UnicastAddresses)
            {
                var address = entry.Address;
                if (address.AddressFamily == AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(address) && !addresses.Contains(address.ToString()))
                    addresses.Add(address.ToString());
            }
        }
        catch (Exception) { }
        return addresses.Count == 0 ? "check headset Wi-Fi settings" : string.Join(", ", addresses);
    }

    private static string ShortMessage(string message)
    {
        string firstLine = message.Split('\n')[0].Trim();
        return firstLine.Length <= 300 ? firstLine : firstLine.Substring(0, 300) + "...";
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log($"[NetworkLauncher] {message}");
    }

    public void TestButton() => SetStatus("BUTTON CLICKED!");
}
