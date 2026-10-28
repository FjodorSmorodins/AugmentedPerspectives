# Shared-room calibration lobby

## Using the game

1. Put two identifiable marks on the physical floor. Use one as the centre of the 2 x 2 metre play area and the other as its forward direction. Separate them by at least 50 cm; about one metre improves directional accuracy.
2. Connect both headsets to the same network. Press Host on Quest Pro and Join on Quest 3. Join discovers the available host automatically; no IP address entry is needed.
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

## Watching the other player calibrate

The other player's calibration dots are actual world-space spheres on the floor. There is no floating map or vector panel. Host dots are yellow; joining-player dots are purple. A smaller sphere shows the other player's moving hand-projected dot, and larger spheres remain at the two pinched positions until play starts. They have no enabled colliders and cannot interfere with grabbing.

`Assets/Scripts/CalibrationPreview.cs` remains attached to Assembly Manager, but now renders floor objects instead of UI. CalibrationLobby.CapturePreview supplies actual Vector3 game-world coordinates and placed/confirmed flags. Changes are sent to the host at up to 15 Hz, and server-written NetworkVariables distribute each player's dots to the other headset, including late joiners. Retry clears that player's placed dots. Disconnect clears the departed joining player's markers. The markers hide when gameplay starts.

Confirming calibration transforms the local stored points using the same rotation and translation applied to OVRCameraRig. Their updated coordinates are shared as well, so markers remain consistent with the newly aligned game space. Local placed dots remain visible while waiting for the other player to finish.

Before both headsets are calibrated, the same game-world coordinate can still appear at different physical spots. Showing networked floor objects does not itself provide physical alignment; both players must select the same real floor marks and confirm. Continue using physical marks as the calibration reference.

Rebuild both headsets. Compilation and headset validation have not been performed for this change.
## Consistent placed-dot display

Both headsets now draw both players' placed dots from the same server-held snapshots. The immediate local placed-dot meshes are suppressed while connected. A separate scene-root object with an identity transform holds the displayed spheres, so a moving manager or tracking parent cannot offset them. The wearer's moving hand cursor remains immediate for responsive placement.

The lobby reports **Shared dot spacing: P1 ... cm | P2 ... cm** after both players have placed the relevant points. Once network updates settle, this numerical distance should agree on both headsets. Visual overlap from one viewing angle does not by itself establish matching coordinates. The physical tape marks remain the calibration reference before both players align.

`[CalibrationDots]` logs record each peer's build GUID and the server's accepted placed-point coordinates. These are included in NetworkLauncher log files. Install the same newly built APK on both headsets when checking this change. No headset was connected when this display change was made; the reported offset has not been reproduced or verified as fixed.
## Disconnect or cancel

Both headsets have a **Disconnect / Cancel** button at the bottom of the floating connection controls. It remains visible while calibrating, waiting for another player, and during gameplay. It calls NetworkLauncher.StopConnection, which stops a connected session or an unfinished join/hosting attempt. Calibration is cleared locally, and Host/Join can be used again after shutdown finishes. If the host disconnects, the session ends for the joining player as well; if the joining player disconnects, the host stays available in the lobby.

The button is created by CalibrationLobby.CreateInterface under ConnectionCanvas, outside the calibration-only group. It uses the existing Meta hand/controller UI interactions. Cancellation-related network callbacks do not overwrite the intentional disconnect status.
## Three-page introduction

After hosting or joining, each player independently reads a three-page introduction before dot placement is enabled. Back and Next navigate the pages; the final page has Continue. The text explains why calibration is needed, how to place the two dots with the right hand and pinching, and how the first dot defines the centre while the direction to the second defines facing. The last page also explains Confirm position, Retry, and waiting for both players.

Page 3 contains a generated top-down UI diagram with points 1 and 2, their direction arrow, the 2 x 2 metre marking, reference area, assembly area, and loose parts. The diagram projects the current scene renderer bounds onto a floor map using CalibrationLobby.gameForward. It therefore reflects the existing relative scene placement, including areas currently outside the red marking. The distance to diagram point 2 is illustrative, not a required exact distance or a way to scale the square.

CalibrationInstructions.cs creates the pages and diagram at runtime inside ConnectionCanvas. CalibrationLobby creates this helper, controls session/read state, and prevents pinches from placing dots while the pages are open. Continue starts placement; reopening Instructions during placement preserves the existing points. Disconnect / Cancel stays outside both the introduction and calibration-only groups, so it remains available throughout. No new scene component, image package, or networking package is required. No Unity build or hardware verification was run for this change.
## Instruction navigation

Back, Next and Continue share a 0.4-second navigation guard. This prevents duplicated Meta UI click events from skipping page 2 during a single pinch or controller press. Opening the introduction also ignores input for 0.25 seconds, so the gesture that opened it cannot advance a page. The connection canvas and Meta BoundsClipper explicitly cover the full 600 x 850 instruction surface, and generated UI anchors/pivots are centred. Accepted clicks and page changes are logged with the CalibrationInstructions prefix in NetworkLogs. This correction has not been built or checked on a headset.