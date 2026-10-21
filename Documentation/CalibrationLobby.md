# Shared-room calibration lobby

## Using the game

1. Put two identifiable marks on the physical floor. Use one as the centre of the 2 x 2 metre play area and the other as its forward direction. Separate them by at least 50 cm; about one metre improves directional accuracy.
2. Connect both headsets to the same network. Start Host on Quest Pro and Join on Quest 3, using the host's current Wi-Fi address.
3. Each player presses **Set floor marks / Retry** in their floating lobby interface.
4. Move your right hand horizontally above the first physical mark. The yellow dot follows its X/Z position while staying 1.5 cm above the tracking floor, regardless of hand height or aim. Pinch to place the dot. The left hand is used if the right hand is not tracked. Controllers also work: the dot follows the controller position and the index trigger confirms.
5. Move your hand above the SAME second physical mark and pinch again to establish direction. The first dot stays fixed while placing the second. Both players must select the marks in the same order.
6. Press **Confirm position**. The local tracking rig aligns to the shared game coordinates. The panel shows readiness while waiting for the other player.
7. When both connected players have confirmed, the parts and assembly controls appear and play begins automatically.

If the alignment is wrong before confirming, press **Set floor marks / Retry**. Recentring the headset reopens calibration and pauses play. A player disconnect also pauses the game; reconnect and confirm again on that player. The remaining player can keep their confirmed calibration.

The lobby interface is positioned within reach near the player's current view when calibration opens. It stays at that pose rather than following every head turn. During play, connection controls return to their saved position inside the floor marking.

## Implementation

`Assets/Scripts/CalibrationLobby.cs` is attached to the existing Assembly Manager scene NetworkObject. Netcode replicates readiness and the started flag. The server requires at least two connected players and waits until every connected player is ready.

For each headset, the selected origin and direction define a yaw correction and translation. The script applies that correction to OVRCameraRig. The physical origin maps to the play-area centre; the selected forward direction maps to the scene's configured Game Forward (-X). Networked object coordinates remain common across both headsets.

Rendering is hidden using Renderer.enabled, leaving scene NetworkObjects active so Netcode can spawn and synchronize them normally. Parts are kinematic and cannot be grabbed until play starts. Scoring, submission, reset and player markers also respect the lobby gate. Existing host/join reference visibility rules still apply after the lobby releases the game.

FloorPlayArea.autoCenterPlayer is disabled in this scene. Its previous automatic centring would overwrite manual calibration. The Locomotor scene instance is disabled to prevent stick movement and teleporting from moving the tracking origin away from its calibrated pose. The floor marking is level with the tracking floor.

The lobby uses the existing connection canvas and its Meta ray interaction components. It creates instructions and Confirm/Retry buttons at runtime. It does not need spatial-anchor services or another networking package.

## Limits

Both players must be in the same physical room, select the same real marks, and use floor-level tracking. Hand placement accuracy determines alignment accuracy. Raising or lowering the hand does not change the dots' fixed floor height. This is manual calibration, not an automatically localized shared spatial anchor. It does not scan physical walls or tables. Headset validation and a Unity build are still required.