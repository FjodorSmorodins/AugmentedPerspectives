using Oculus.Interaction;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Grabbable), typeof(Rigidbody))]
public class NetworkGrabOwnership : NetworkBehaviour
{
    private Grabbable grabbable;
    private Rigidbody body;
    private AssemblePart part;
    private bool originalKinematic;
    private readonly NetworkVariable<bool> snapped = new(false);

    private void Awake()
    {
        grabbable = GetComponent<Grabbable>();
        body = GetComponent<Rigidbody>();
        part = GetComponent<AssemblePart>();
        originalKinematic = body.isKinematic;
    }

    private void OnEnable()
    {
        grabbable.WhenPointerEventRaised += HandlePointerEvent;
    }

    private void OnDisable()
    {
        grabbable.WhenPointerEventRaised -= HandlePointerEvent;
    }

    public override void OnNetworkSpawn()
    {
        snapped.OnValueChanged += HandleSnapChanged;
        part?.ApplyNetworkSnapState(snapped.Value);
        ApplyPhysicsAuthority();
        Debug.Log($"[NetworkGrab] Spawned {name}: local={NetworkManager.LocalClientId}, owner={OwnerClientId}");
        if (grabbable.SelectingPointsCount > 0)
            BeginLocalGrab();
    }

    public override void OnNetworkDespawn()
    {
        snapped.OnValueChanged -= HandleSnapChanged;
        body.isKinematic = originalKinematic || grabbable.SelectingPointsCount > 0 ||
                           (part != null && part.IsLocked);
    }

    private void HandlePointerEvent(PointerEvent pointerEvent)
    {
        if (pointerEvent.Type == PointerEventType.Select && grabbable.SelectingPointsCount == 1)
            BeginLocalGrab();
        else if ((pointerEvent.Type == PointerEventType.Unselect || pointerEvent.Type == PointerEventType.Cancel) &&
                 grabbable.SelectingPointsCount == 0)
            EndLocalGrab();
        ApplyPhysicsAuthority();
    }

    public void BeginLocalGrab()
    {
        Debug.Log($"[NetworkGrab] Grab {name}: spawned={IsSpawned}, owner={OwnerClientId}");
        if (!IsSpawned || IsOwner)
            return;
        RequestOwnershipRpc();
    }

    public void EndLocalGrab()
    {
        // Keep ownership after release so that the same peer simulates the throw.
        Debug.Log($"[NetworkGrab] Release {name}: owner={OwnerClientId}");
        ApplyPhysicsAuthority();
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void RequestOwnershipRpc(RpcParams rpcParams = default)
    {
        ulong requestingClientId = rpcParams.Receive.SenderClientId;
        if ((part != null && (part.IsLocked || !part.CanClientGrab(requestingClientId))) ||
            !NetworkManager.ConnectedClients.ContainsKey(requestingClientId))
            return;
        if (NetworkObject.OwnerClientId != requestingClientId)
        {
            Debug.Log($"[NetworkGrab] Grant {name}: {OwnerClientId} -> {requestingClientId}");
            NetworkObject.ChangeOwnership(requestingClientId);
        }
    }

    public void RequestSnap(Vector3 releasedPosition)
    {
        if (IsSpawned)
            RequestSnapRpc(releasedPosition);
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void RequestSnapRpc(Vector3 releasedPosition, RpcParams rpcParams = default)
    {
        if (part == null || part.IsLocked || !part.CanClientGrab(rpcParams.Receive.SenderClientId) ||
            OwnerClientId != rpcParams.Receive.SenderClientId ||
            !part.IsWithinSnapDistance(releasedPosition))
            return;
        snapped.Value = true;
    }

    private void HandleSnapChanged(bool previous, bool current)
    {
        part?.ApplyNetworkSnapState(current);
        ApplyPhysicsAuthority();
    }

    public void ResetNetworkSnap()
    {
        if (IsSpawned && IsServer)
            snapped.Value = false;
    }

    protected override void OnOwnershipChanged(ulong previous, ulong current)
    {
        base.OnOwnershipChanged(previous, current);
        ApplyPhysicsAuthority();
        Debug.Log($"[NetworkGrab] Ownership {name}: {previous} -> {current}, local={NetworkManager.LocalClientId}");
    }

    private void LateUpdate()
    {
        ApplyPhysicsAuthority();
    }

    private void ApplyPhysicsAuthority()
    {
        if (!IsSpawned)
            return; // Allow the original offline grab behaviour before connecting.

        // Meta's Grabbable changes kinematic state during selection and release.
        // Reconcile that with ownership: remote replicas must never run gravity,
        // and an ownership grant must not turn a held object into a falling body.
        bool kinematic = !IsOwner || originalKinematic || grabbable.SelectingPointsCount > 0 ||
                         (part != null && part.IsLocked);
        if (body.isKinematic != kinematic)
            body.isKinematic = kinematic;
    }
}
