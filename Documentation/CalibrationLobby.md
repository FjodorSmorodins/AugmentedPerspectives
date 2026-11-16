# Guided shared-room setup

## Player sequence

The connection controls, setup guide, and assembly controls share one script-sized touchscreen and one pedestal. Before connecting, Host, Join, and connection status are shown. While setting up, the guide uses the whole screen. Disconnect / Cancel remains at the bottom throughout. Each player follows the guide independently in the same lobby.

1. **Clear a space.** Clear a flat 2 × 2 m space where the game will go, then choose **Set position**.
2. **Set the position.** Move your hand or controller to slide the layout. Hold a pinch or the trigger for three seconds to set the first point. Both players use the same floor spot.
3. **Wait for the first point.** The first player can choose **Change position** and place the point again. Both players advance to rotation when both first points are set.
4. **Set the rotation.** Move your hand or controller to turn the layout around the first point. Pinch or press the trigger to set the second point at the same floor spot as your partner.
5. **Wait for the second point.** Choose **Change rotation** to redo the second point while waiting. Both players advance to the layout check when both second points are set.
6. **Check the layout.** Choose **Confirm calibration**, **Change rotation**, or **Change position**. Changing position requires placing both points again. Either change clears final approvals for the previous layout. The game starts once both players confirm the current layout.

## Full-size layout preview

CalibrationLayoutPreview builds wire boxes from the actual renderer bounds of the reference/example area, assembly area, and loose parts. Coloured floor footprints and labels identify **Example to copy**, **Build here**, and **Loose pieces**. A 2 by 2 metre square and an arrow show the chosen middle and facing direction. The preview includes both players' scene areas so the shared layout can be checked before confirming.

The preview is a local visual aid with no colliders, Rigidbody, grab components, or NetworkObjects. It snapshots the current scene on entry to first-dot placement. The outline follows the tracked cursor during that step, then holds the same position and facing when the player places dot 1. Its transformation is the inverse of the rig correction applied at final confirmation, so it shows where the real scene will appear after alignment. Dot spacing chooses direction only.

The preview reflects the scene's actual scale and relative positions, including objects outside the 2 by 2 metre square. It does not shrink them into that square or scan the real room for obstacles. The player must check the entire outline has room. The normal red floor marking stays hidden before play so an unaligned marking cannot be confused with the chosen preview square.

The outline stays visible after the first point, during second-point placement, and during the layout check and waiting screens. Once confirmed, its coordinates are transformed with the headset alignment so it stays at the same physical location. Losing hand or controller tracking before dot 1 hides the moving outline until tracking returns. Recentring or leaving the session hides it. The actual game renderers remain hidden and grabbing/scoring stays gated until play starts.

## Implementation and networking

CalibrationGuide derives the screen from the player's local progress and the latest checkpoint published by the host. CalibrationSession tracks each connected player's progress and publishes the lowest completed checkpoint once two players are present. A changed point or disconnect revokes final approvals and increments the revision. The host accepts final confirmations for the current revision once both players have placed both points.

CalibrationLobby handles headset input, the preview, and the checkpoints. Point one requires a continuous three-second hold from the same tracked hand or controller. Releasing, losing tracking, or pointing at the touchscreen cancels the hold. Point two uses a single pinch or trigger press. UI and floor actions share a half-second guard to separate successive gestures.

After both players confirm, each headset maps its first point to the play-area centre and maps its selected direction to gameForward (-X in BuildAsssemblyGame). The display then returns to its gameplay position. Recentring restarts that player's setup. If the other player disconnects, the remaining player keeps their points and waits for the returning player to catch up before confirming again.

Each player sees their own placed dots, moving cursor, and layout outline. The host sees yellow dots and the joining player sees purple dots. CalibrationPreview shares coordinate snapshots for diagnostics. Players agree on the same two physical floor spots together.

## Validation

`Tests/CalibrationGuide` exercises both players through the host's checkpoints, edits from the waiting and review screens, stale final confirmations, and disconnect recovery. The three-second hold and screen layout require a headset check.

### Gameplay guide and snapped completion

After both players finish calibration, `CalibrationLobby` creates/shows `GameplayGuide` on the existing interface stand. Three short pages explain role-based grabbing (seeing a reference means your partner moves its copy), verbal cooperation, and releasing parts to snap. Back/Next/Read again are local to each headset, with a 0.5-second click guard. Players can assemble while reading; the guide shows live snapped-part progress and completion. Reset appears below the guide, above Disconnect. It teleports all unsnapped pieces to their starting positions and rotations. Snapped pieces stay in place.

`AssemblyManager` now requires every entry in its parts array to be non-null and `IsSnapped` before completion. An empty array cannot complete. The host publishes completion; clients also wait for their local replicated snap states before playing the sound/particles and displaying COMPLETED. Accuracy remains informational. Completion follows when all pieces snap into place. Compound visual children are covered by their parent assembly part's snap.

These changes have not been built or tested on the headsets.
