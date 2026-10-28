using System;
using Unity.Netcode;
using UnityEngine;

// Replicates calibration dots as world-space objects. Physical alignment still
// depends on each headset completing the shared two-point calibration.
public class CalibrationPreview : NetworkBehaviour
{
    public struct State : INetworkSerializable, IEquatable<State>
    {
        public Vector3 Origin, Direction, Cursor;
        public bool Present, HasOrigin, HasDirection, CursorVisible, Confirmed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref Direction);
            serializer.SerializeValue(ref Cursor);
            serializer.SerializeValue(ref Present);
            serializer.SerializeValue(ref HasOrigin);
            serializer.SerializeValue(ref HasDirection);
            serializer.SerializeValue(ref CursorVisible);
            serializer.SerializeValue(ref Confirmed);
        }

        public bool Equals(State other) => Origin.Equals(other.Origin) && Direction.Equals(other.Direction) &&
            Cursor.Equals(other.Cursor) && Present == other.Present && HasOrigin == other.HasOrigin &&
            HasDirection == other.HasDirection && CursorVisible == other.CursorVisible && Confirmed == other.Confirmed;
    }

    [SerializeField] private CalibrationLobby lobby;
    [SerializeField] private Material hostMaterial;
    [SerializeField] private Material joiningMaterial;
    private readonly NetworkVariable<State> hostState = new(default);
    private readonly NetworkVariable<State> joinState = new(default);
    private NetworkManager subscribedManager;
    private ulong joiningClientId;
    private float nextSend;
    private Transform firstDot, secondDot, liveDot, joiningFirstDot, joiningSecondDot, joiningLiveDot;
    private GameObject markerRoot;
    private Material appliedLocalMaterial;
    private State lastSent;
    private bool sent;

    public override void OnNetworkSpawn()
    {
        sent = false;
        nextSend = 0f;
        ReportBuildRpc(Application.buildGUID);
        if (!IsServer) return;
        hostState.Value = default;
        joinState.Value = default;
        subscribedManager = NetworkManager;
        subscribedManager.OnClientDisconnectCallback += OnDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        Unsubscribe();
        sent = false;
        SetDotsVisible(false);
        if (lobby != null) lobby.SetReplicatedPlacedMarkers(false);
    }

    private void OnDisconnected(ulong clientId)
    {
        if (IsSpawned && clientId == joiningClientId) joinState.Value = default;
    }

    private void Unsubscribe()
    {
        if (subscribedManager != null) subscribedManager.OnClientDisconnectCallback -= OnDisconnected;
        subscribedManager = null;
    }

    private void LateUpdate()
    {
        if (lobby == null) return;
        if (firstDot == null) CreateDots();
        bool visible = IsSpawned && !CalibrationLobby.CanPlay;
        if (!IsSpawned || NetworkManager.ShutdownInProgress)
        {
            SetDotsVisible(false);
            return;
        }
        lobby.SetReplicatedPlacedMarkers(true);
        Material localMaterial = IsHost ? hostMaterial : joiningMaterial;
        if (localMaterial != appliedLocalMaterial && localMaterial != null)
        {
            lobby.SetCalibrationMarkerMaterial(localMaterial);
            appliedLocalMaterial = localMaterial;
        }
        State local = lobby.CapturePreview();
        // Keep publishing changed final coordinates after alignment as well.
        if (Time.unscaledTime >= nextSend && (!sent || !local.Equals(lastSent)))
        {
            nextSend = Time.unscaledTime + 1f / 15f;
            lastSent = local;
            sent = true;
            PublishRpc(local);
        }
        Draw(hostState.Value, joinState.Value, visible);
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void ReportBuildRpc(string buildGuid, RpcParams rpcParams = default)
    {
        Debug.Log($"[CalibrationDots] Peer {rpcParams.Receive.SenderClientId}: " +
                  $"world-dot protocol=2, build={buildGuid}; host build={Application.buildGUID}");
    }

    public string ComparisonText()
    {
        if (!IsSpawned || CalibrationLobby.CanPlay) return "";
        State host = hostState.Value;
        State join = joinState.Value;
        if (!host.Present || !join.Present || !host.HasOrigin || !join.HasOrigin) return "";
        float firstCm = Vector3.Distance(host.Origin, join.Origin) * 100f;
        string text = $"\nShared dot spacing: P1 {firstCm:0.0} cm";
        if (host.HasDirection && join.HasDirection)
            text += $" | P2 {Vector3.Distance(host.Direction, join.Direction) * 100f:0.0} cm";
        return text;
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void PublishRpc(State state, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.ContainsKey(sender) ||
            !Finite(state.Origin) || !Finite(state.Direction) || !Finite(state.Cursor)) return;
        State previous = sender == Unity.Netcode.NetworkManager.ServerClientId ? hostState.Value : joinState.Value;
        if (state.HasOrigin != previous.HasOrigin || state.HasDirection != previous.HasDirection ||
            state.Confirmed != previous.Confirmed || (state.HasOrigin && state.Origin != previous.Origin) ||
            (state.HasDirection && state.Direction != previous.Direction))
            Debug.Log($"[CalibrationDots] Accepted peer={sender}, P1={state.Origin:F4}, P2={state.Direction:F4}, " +
                      $"placed1={state.HasOrigin}, placed2={state.HasDirection}, confirmed={state.Confirmed}");
        state.Present = true;
        if (sender == Unity.Netcode.NetworkManager.ServerClientId) hostState.Value = state;
        else
        {
            joiningClientId = sender;
            joinState.Value = state;
        }
    }

    private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) &&
        !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);

    private void Draw(State host, State join, bool visible)
    {
        // Both headsets render BOTH players' placed dots from the same network
        // snapshots. Never mix an immediate local placed dot with an echoed one.
        PlaceDot(firstDot, visible && host.Present && host.HasOrigin, host.Origin, hostMaterial);
        PlaceDot(secondDot, visible && host.Present && host.HasDirection, host.Direction, hostMaterial);
        PlaceDot(joiningFirstDot, visible && join.Present && join.HasOrigin, join.Origin, joiningMaterial);
        PlaceDot(joiningSecondDot, visible && join.Present && join.HasDirection, join.Direction, joiningMaterial);
        // Keep the wearer's hand cursor immediate; only the other cursor is replicated.
        PlaceDot(liveDot, visible && !IsHost && host.Present && host.CursorVisible, host.Cursor, hostMaterial);
        PlaceDot(joiningLiveDot, visible && IsHost && join.Present && join.CursorVisible, join.Cursor, joiningMaterial);
    }

    private static void PlaceDot(Transform dot, bool visible, Vector3 position, Material material)
    {
        dot.gameObject.SetActive(visible);
        if (!visible) return;
        dot.position = position + Vector3.up * 0.015f;
        dot.GetComponent<Renderer>().sharedMaterial = material;
    }

    private void SetDotsVisible(bool visible)
    {
        if (firstDot != null) firstDot.gameObject.SetActive(visible);
        if (secondDot != null) secondDot.gameObject.SetActive(visible);
        if (liveDot != null) liveDot.gameObject.SetActive(visible);
        if (joiningFirstDot != null) joiningFirstDot.gameObject.SetActive(visible);
        if (joiningSecondDot != null) joiningSecondDot.gameObject.SetActive(visible);
        if (joiningLiveDot != null) joiningLiveDot.gameObject.SetActive(visible);
    }

    private void CreateDots()
    {
        markerRoot = new GameObject("Shared Calibration Dots - World Coordinates");
        markerRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        firstDot = CreateDot("Host Floor Point 1", 0.05f);
        secondDot = CreateDot("Host Floor Point 2", 0.05f);
        liveDot = CreateDot("Host Moving Floor Dot", 0.025f);
        joiningFirstDot = CreateDot("Joining Player Floor Point 1", 0.04f);
        joiningSecondDot = CreateDot("Joining Player Floor Point 2", 0.04f);
        joiningLiveDot = CreateDot("Joining Player Moving Floor Dot", 0.025f);
    }

    private Transform CreateDot(string label, float diameter)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = label;
        go.transform.SetParent(markerRoot.transform, false);
        go.transform.localScale = Vector3.one * diameter;
        Collider collider = go.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.SetActive(false);
        return go.transform;
    }

    public override void OnDestroy()
    {
        Unsubscribe();
        if (lobby != null) lobby.SetReplicatedPlacedMarkers(false);
        if (markerRoot != null) Destroy(markerRoot);
        base.OnDestroy();
    }
}