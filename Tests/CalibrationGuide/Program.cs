using Stage = CalibrationGuide.Stage;
using Progress = CalibrationGuide.Progress;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void At(CalibrationGuide guide, Stage stage) => Check(guide.Step == stage, $"Expected {stage}, got {guide.Step}");

const ulong host = 0, guest = 1;
var session = new CalibrationSession();
var first = new CalibrationGuide();
var second = new CalibrationGuide();
void Sync()
{
    first.UpdateSharedProgress(session.SharedProgress, session.Revision);
    second.UpdateSharedProgress(session.SharedProgress, session.Revision);
}
void Send(CalibrationGuide guide, ulong player)
{
    Check(session.SetProgress(player, guide.LocalProgress, session.Revision), $"Host rejected {guide.LocalProgress} for {player}");
    Sync();
}
void Place(CalibrationGuide guide, ulong player)
{
    Check(guide.PlacePoint(), "This stage should accept a point");
    Send(guide, player);
}

session.Connect(host);
At(first, Stage.ChooseArea);
Check(!first.PlacePoint(), "Clear-space page must block floor input");
Check(first.Advance(), "Set position opens placement");
At(first, Stage.PlaceFirst);
Place(first, host);
At(first, Stage.WaitForPosition);
Check(!first.PlacePoint() && !first.Advance(), "One player cannot advance to rotation alone");
Check(first.ChangePosition(), "Change position while waiting");
Send(first, host);
At(first, Stage.PlaceFirst);
Check(!first.HasOrigin && !first.HasDirection, "Changing position clears both dots");
Place(first, host);

session.Connect(guest);
Sync();
Check(!session.SetProgress(guest, Progress.Rotation, session.Revision), "A peer cannot skip its first point");
Check(second.Advance(), "Joining player can start setting position");
Place(second, guest);
At(first, Stage.PlaceSecond);
At(second, Stage.PlaceSecond);
Place(first, host);
At(first, Stage.WaitForRotation);
Check(!first.Advance(), "One rotation cannot open final approval");
Check(first.ChangeRotation(), "Change rotation while waiting");
Send(first, host);
At(first, Stage.PlaceSecond);
Check(first.HasOrigin && !first.HasDirection, "Changing rotation preserves the first dot");
Place(first, host);
Place(second, guest);
At(first, Stage.CheckLayout);
At(second, Stage.CheckLayout);

Check(first.Advance(), "Host can confirm the reviewed layout");
Send(first, host);
At(first, Stage.Waiting);
Check(session.SharedProgress == Progress.Rotation, "Gameplay requires both final approvals");
uint previousRevision = session.Revision;
Check(second.ChangeRotation(), "Final review allows changing rotation");
Send(second, guest);
At(first, Stage.WaitForRotation);
Check(!first.Confirmed, "A rotation edit revokes the partner's final approval");
Place(second, guest);
At(first, Stage.CheckLayout);
Check(!session.SetProgress(host, Progress.Confirmed, previousRevision), "A delayed approval cannot approve a revised layout");
Check(first.Advance(), "Host approves revised layout");
Send(first, host);
Check(second.ChangePosition(), "Final review allows changing position");
Send(second, guest);
At(first, Stage.WaitForPosition);
At(second, Stage.PlaceFirst);
Check(!first.Confirmed && !second.HasDirection, "A position edit invalidates approval and requires the editor to replace both points");
Place(second, guest);
At(second, Stage.PlaceSecond);
Place(second, guest);
At(first, Stage.CheckLayout);
At(second, Stage.CheckLayout);
Check(first.Advance() && second.Advance(), "Both can approve after the edits");
Send(first, host);
Send(second, guest);
Check(session.SharedProgress == Progress.Confirmed, "Both approvals release gameplay");

session.Disconnect(guest);
Sync();
At(first, Stage.WaitForPosition);
Check(first.HasOrigin && first.HasDirection && !first.Confirmed, "Disconnect pauses the session and preserves the remaining player's dots");
Check(!session.SetProgress(guest, Progress.None, session.Revision), "A disconnected peer cannot update progress");
first.Restart();
At(first, Stage.ChooseArea);
Check(!first.HasOrigin && !first.HasDirection && !first.Confirmed, "Restart clears local calibration");
Console.WriteLine($"PASS: {checks} calibration checkpoint checks.");
