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

    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            SetStatus("ERROR: NetworkManager not found.");
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback +=
            HandleClientConnected;

        NetworkManager.Singleton.OnClientDisconnectCallback +=
            HandleClientDisconnected;

        SetStatus("Choose Host or Join.");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -=
            HandleClientConnected;

        NetworkManager.Singleton.OnClientDisconnectCallback -=
            HandleClientDisconnected;
    }

    public void StartHost()
    {
        SetStatus("Starting host...");

        if (NetworkManager.Singleton == null)
        {
            SetStatus("ERROR: NetworkManager not found.");
            return;
        }

        UnityTransport transport =
            NetworkManager.Singleton.GetComponent<UnityTransport>();

        if (transport == null)
        {
            SetStatus("ERROR: UnityTransport not found.");
            return;
        }

        transport.SetConnectionData(
            "0.0.0.0",
            port,
            "0.0.0.0");

        bool started = NetworkManager.Singleton.StartHost();

        if (started)
            SetStatus("Hosting. Waiting for player...");
        else
            SetStatus("ERROR: Host failed to start.");
    }

    public void StartClient()
    {
        SetStatus($"Joining {hostAddress}...");

        if (NetworkManager.Singleton == null)
        {
            SetStatus("ERROR: NetworkManager not found.");
            return;
        }

        UnityTransport transport =
            NetworkManager.Singleton.GetComponent<UnityTransport>();

        if (transport == null)
        {
            SetStatus("ERROR: UnityTransport not found.");
            return;
        }

        transport.SetConnectionData(hostAddress, port);

        bool started = NetworkManager.Singleton.StartClient();

        if (!started)
            SetStatus("ERROR: Join attempt could not start.");
    }

    public void StopConnection()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();

        SetStatus("Disconnected.");
    }

    private void HandleClientConnected(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null)
            return;

        if (manager.IsHost)
        {
            if (clientId == manager.LocalClientId)
                SetStatus("Hosting. Waiting for player...");
            else
                SetStatus("Player connected!");
        }
        else if (clientId == manager.LocalClientId)
        {
            SetStatus("Connected to host!");
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null)
        {
            SetStatus("Disconnected.");
            return;
        }

        if (clientId == manager.LocalClientId)
        {
            SetStatus("Disconnected from session.");
        }
        else if (manager.IsHost)
        {
            SetStatus("Other player disconnected.");
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log(message);
    }

    public void TestButton()
    {
        SetStatus("BUTTON CLICKED!");
    }
}