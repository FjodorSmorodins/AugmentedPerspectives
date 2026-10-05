using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkGrabOwnership : NetworkBehaviour
{
    public void BeginLocalGrab()
    {
        if (!IsSpawned)
            return;

        RequestOwnershipRpc(NetworkManager.Singleton.LocalClientId);
    }

    public void EndLocalGrab()
    {
        // Initially keep ownership after release.
        // This prevents an unnecessary snap during release.
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void RequestOwnershipRpc(ulong requestingClientId)
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();

        // Simple first version: latest grab request wins.
        if (networkObject.OwnerClientId != requestingClientId)
            networkObject.ChangeOwnership(requestingClientId);
    }
}