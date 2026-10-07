using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkLauncher : MonoBehaviour
{
    [Header("Connection")]
    [SerializeField] private string hostAddress = "192.168.1.100";
    [SerializeField] private ushort port = 7777;

    [Header("Interface")]
    [SerializeField] private TMP_Text statusText;

    private NetworkManager manager;
    private StreamWriter logWriter;
    private bool starting;
    private bool startupFailed;
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
        manager = NetworkManager.Singleton;
        if (manager == null)
        {
            SetStatus("ERROR: NetworkManager not found.");
            return;
        }

        manager.LogLevel = Unity.Netcode.LogLevel.Developer;
        manager.OnClientConnectedCallback += HandleClientConnected;
        manager.OnClientDisconnectCallback += HandleClientDisconnected;
        manager.OnTransportFailure += HandleTransportFailure;
        Debug.Log($"[NetworkLauncher] Local IPv4: {LocalAddresses()}; " +
                  $"join target: {hostAddress}:{port}; Unity {Application.unityVersion}");
        SetStatus("Choose Host or Join.");
    }

    private void OnDestroy()
    {
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
    public void StartClient() => StartConnection(false);

    private void StartConnection(bool asHost)
    {
        if (manager == null)
        {
            SetStatus("ERROR: NetworkManager not found.");
            return;
        }
        if (starting || manager.ShutdownInProgress)
        {
            SetStatus("Please wait for the current connection operation.");
            return;
        }
        if (manager.IsListening)
        {
            Debug.Log("[NetworkLauncher] Ignored repeated start request: networking is already running.");
            return;
        }
        UnityTransport transport = manager.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null)
        {
            SetStatus("ERROR: Assign UnityTransport in NetworkManager's Network Transport field.");
            return;
        }
        if (!asHost && (!System.Net.IPAddress.TryParse(hostAddress.Trim(), out var address) ||
                        address.AddressFamily != AddressFamily.InterNetwork ||
                        address.Equals(System.Net.IPAddress.Any) ||
                        System.Net.IPAddress.IsLoopback(address)))
        {
            SetStatus("ERROR: Set Host Address to the hosting headset's Wi-Fi IPv4 address.");
            return;
        }

        startupError = null;
        startupFailed = false;
        starting = true;
        try
        {
            // The host's local client connects to loopback; the server listens on all interfaces.
            transport.SetConnectionData(asHost ? "127.0.0.1" : hostAddress.Trim(), port, "0.0.0.0");
            Debug.Log($"[NetworkLauncher] Starting {(asHost ? "Host" : "Client")}: " +
                      $"address={transport.ConnectionData.Address}:{port}, " +
                      $"listen={transport.ConnectionData.ServerListenAddress}, " +
                      $"localIPv4={LocalAddresses()}, listening={manager.IsListening}");
            SetStatus(asHost ? "Starting host..." : $"Joining {hostAddress}:{port}...");
            bool started = asHost ? manager.StartHost() : manager.StartClient();
            if (!started)
            {
                startupFailed = true;
                string reason = startupError ?? "No underlying error was reported; collect the NetworkLogs file or adb logcat.";
                SetStatus($"ERROR: {(asHost ? "Host" : "Client")} failed to start.\n{ShortMessage(reason)}");
            }
            else if (asHost)
            {
                SetStatus($"Hosting. Join address: {LocalAddresses()}:{port}\nWaiting for player...");
            }
        }
        catch (Exception exception)
        {
            startupFailed = true;
            Debug.LogException(exception);
            SetStatus($"ERROR: Connection startup threw an exception.\n{ShortMessage(exception.Message)}");
        }
        finally
        {
            starting = false;
        }
    }

    public void StopConnection()
    {
        startupFailed = false;
        if (manager != null)
            manager.Shutdown();
        SetStatus("Disconnected.");
    }

    private void HandleTransportFailure()
    {
        Debug.LogError("[NetworkLauncher] UnityTransport failed. See the preceding socket/initialization error.");
        if (!starting)
            SetStatus("ERROR: Transport failed. See NetworkLogs or adb logcat.");
    }

    private void HandleClientConnected(ulong clientId)
    {
        Debug.Log($"[NetworkLauncher] Client connected: {clientId}; local={manager.LocalClientId}");
        if (manager.IsHost)
        {
            SetStatus(clientId == manager.LocalClientId
                ? $"Hosting. Join address: {LocalAddresses()}:{port}\nWaiting for player..."
                : "Player connected!");
        }
        else if (clientId == manager.LocalClientId)
        {
            SetStatus("Connected to host!");
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        string reason = manager != null ? manager.DisconnectReason : "";
        Debug.LogWarning($"[NetworkLauncher] Client disconnected: {clientId}; reason={reason}");
        if (starting || startupFailed)
            return; // Keep startup failure visible instead of replacing it with 'Disconnected'.
        if (manager == null || clientId == manager.LocalClientId)
            SetStatus("Disconnected from session." + (string.IsNullOrEmpty(reason) ? "" : $"\n{ShortMessage(reason)}"));
        else if (manager.IsHost)
            SetStatus("Other player disconnected.");
    }

    private void CaptureLog(string message, string stackTrace, LogType type)
    {
        bool error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        if (starting && error && startupError == null)
            startupError = message;
        if (!error && type != LogType.Warning && !message.Contains("[NetworkLauncher]") &&
            !message.Contains("[NetworkGrab]") && !message.Contains("Netcode") && !message.Contains("Transport"))
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
