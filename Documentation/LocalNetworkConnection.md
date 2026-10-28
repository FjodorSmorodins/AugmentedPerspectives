# Connecting without editing IP addresses

## Player flow

1. Connect both headsets to the same Wi-Fi network or phone hotspot.
2. On one headset, press **Host**.
3. On the other, press **Join**. The app searches for an available Building Game host and connects automatically.
4. Continue through instructions and calibration as before.
5. **Disconnect / Cancel** stops discovery, an unfinished join, or the running session.

When changing Wi-Fi networks, disconnect the session, connect both headsets to the new network, and use Host and Join again. There is no saved headset IP to update. Either headset can host; the existing visibility/grab rules follow the host/join role.

Join searches for 15 seconds. If no host is available, it reports what to check and allows another search. If multiple hosts answer, it asks players to keep only their intended host running and retry. A host with two connected players does not advertise another free slot; it becomes available again if the joining player disconnects.

## Implementation

- `Assets/Scripts/LanHostDiscovery.cs` is a plain C# helper using a non-blocking IPv4 UDP socket. Join broadcasts a game-specific query on discovery port 47777 once per second, using both limited and available subnet-directed broadcasts. The host replies directly to the querying socket with its session identifier and gameplay port. Join uses the response packet's source address as the current host IP, collects responses briefly to detect multiple hosts, then hands the discovered endpoint to UnityTransport.
- `Assets/Scripts/NetworkLauncher.cs` owns discovery and drives it from Update without background threads or waiting on socket reads. The existing Host, Join, and Disconnect buttons retain their callbacks. Discovery sockets close on cancellation, failed startup, transport failure, local disconnect, or destruction.
- The scene's NetworkLauncher no longer serializes hostAddress. The transport is configured at runtime: the host listens on all interfaces, and Join uses the discovered endpoint. The only numeric loopback address is for the host's own internal client, not the remote headset's IP.
- Android Internet Access is set to Require, and the custom Android manifest includes ACCESS_NETWORK_STATE. These support network access and interface enumeration.
- No cloud service, account, Relay package, port forwarding, or host-IP entry is part of this flow. Normal gameplay still uses the existing Netcode/UnityTransport connection and gameplay port 7777.

## Scope

This is local-network discovery, not internet matchmaking. Both devices must be on the same reachable IPv4 LAN/subnet. Networks that isolate Wi-Fi clients or block broadcasts can prevent discovery or gameplay; this code does not bypass those restrictions. An internet-capable room-code flow would need a relay/session service instead.

References: [Microsoft UDP broadcast behaviour](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient.enablebroadcast), [Unity Android Internet Access](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/playersettings/android/forceinternetpermission).

Compilation, discovery on a Quest hotspot, and the complete two-headset join/cancel flow have not been tested for this change. Install the same newly built APK on both headsets before using the new discovery protocol.