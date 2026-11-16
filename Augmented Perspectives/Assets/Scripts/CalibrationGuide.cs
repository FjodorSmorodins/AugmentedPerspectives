using System.Collections.Generic;

public sealed class CalibrationGuide
{
    public enum Stage { ChooseArea, PlaceFirst, WaitForPosition, PlaceSecond, WaitForRotation, CheckLayout, Waiting }
    public enum Progress : byte { None, Position, Rotation, Confirmed }

    private bool areaChosen;
    private Progress sharedProgress;
    private uint revision;

    public Progress LocalProgress { get; private set; }
    public bool HasOrigin => LocalProgress >= Progress.Position;
    public bool HasDirection => LocalProgress >= Progress.Rotation;
    public bool Confirmed => LocalProgress == Progress.Confirmed;
    public bool Selecting => Step is Stage.PlaceFirst or Stage.PlaceSecond;
    public bool CanChangePosition => Step is Stage.WaitForPosition or Stage.CheckLayout or Stage.Waiting;
    public bool CanChangeRotation => Step is Stage.WaitForRotation or Stage.CheckLayout or Stage.Waiting;

    public Stage Step
    {
        get
        {
            if (!areaChosen) return Stage.ChooseArea;
            if (!HasOrigin) return Stage.PlaceFirst;
            if (sharedProgress < Progress.Position) return Stage.WaitForPosition;
            if (!HasDirection) return Stage.PlaceSecond;
            if (sharedProgress < Progress.Rotation) return Stage.WaitForRotation;
            return Confirmed ? Stage.Waiting : Stage.CheckLayout;
        }
    }

    public void UpdateSharedProgress(Progress progress, uint currentRevision)
    {
        // Any changed dot invalidates final approvals for the previous layout.
        if (revision != currentRevision && Confirmed) LocalProgress = Progress.Rotation;
        sharedProgress = progress;
        revision = currentRevision;
    }

    public bool Advance()
    {
        if (Step == Stage.ChooseArea)
        {
            areaChosen = true;
            return true;
        }
        if (Step != Stage.CheckLayout) return false;
        LocalProgress = Progress.Confirmed;
        return true;
    }

    public bool PlacePoint()
    {
        switch (Step)
        {
            case Stage.PlaceFirst: LocalProgress = Progress.Position; return true;
            case Stage.PlaceSecond: LocalProgress = Progress.Rotation; return true;
            default: return false;
        }
    }

    public bool ChangePosition()
    {
        if (!CanChangePosition) return false;
        LocalProgress = Progress.None;
        return true;
    }

    public bool ChangeRotation()
    {
        if (!CanChangeRotation) return false;
        LocalProgress = Progress.Position;
        return true;
    }

    public void Restart()
    {
        areaChosen = false;
        LocalProgress = Progress.None;
    }

    public string Heading => Step switch
    {
        Stage.ChooseArea => "Clear a space",
        Stage.PlaceFirst => "Set the position",
        Stage.WaitForPosition => "First point placed",
        Stage.PlaceSecond => "Set the rotation",
        Stage.WaitForRotation => "Second point placed",
        Stage.CheckLayout => "Check the layout",
        _ => "Calibration confirmed"
    };

    public string Body => Step switch
    {
        Stage.ChooseArea => "Clear a flat 2 × 2 m space where you want the game to be.",
        Stage.PlaceFirst => "Move your hand to reposition the layout.\nPinch thumb and index for 3 seconds to put first point and confirm the position.\nUse the same spot as your partner.",
        Stage.WaitForPosition => "Wait for the other player. Make sure they put the first point in the same spot.",
        Stage.PlaceSecond => "Move your hand to rotate the area.\nPinch to put second point and confirm the rotation.\nUse the same spot as your partner.",
        Stage.WaitForRotation => "Wait for the other player. Make sure they put the second point in the same spot.",
        Stage.CheckLayout => "Does the layout fit? Did you both use the same two spots?",
        _ => "Wait for the other player to confirm the layout."
    };

    public string PrimaryLabel => Step switch
    {
        Stage.ChooseArea => "Set position",
        Stage.CheckLayout => "Confirm calibration",
        _ => ""
    };
}

// The host tracks both players' checkpoints. A revision identifies the set of
// positions and rotations that a final confirmation applies to.
public sealed class CalibrationSession
{
    private readonly Dictionary<ulong, CalibrationGuide.Progress> players = new();
    private readonly List<ulong> confirmedPlayers = new();
    public uint Revision { get; private set; }

    public CalibrationGuide.Progress SharedProgress
    {
        get
        {
            if (players.Count < 2) return CalibrationGuide.Progress.None;
            var progress = CalibrationGuide.Progress.Confirmed;
            foreach (var player in players.Values)
                if (player < progress) progress = player;
            return progress;
        }
    }

    public void Reset()
    {
        players.Clear();
        Revision = 0;
    }

    public void Connect(ulong player) => players.TryAdd(player, CalibrationGuide.Progress.None);

    public void Disconnect(ulong player)
    {
        if (players.Remove(player)) InvalidateConfirmations();
    }

    public bool SetProgress(ulong player, CalibrationGuide.Progress progress, uint revision)
    {
        if (!players.TryGetValue(player, out var previous) ||
            progress > CalibrationGuide.Progress.Confirmed || (int)progress > (int)previous + 1) return false;
        if (progress == CalibrationGuide.Progress.Confirmed &&
            (revision != Revision || SharedProgress < CalibrationGuide.Progress.Rotation)) return false;
        players[player] = progress;
        if (progress < previous) InvalidateConfirmations();
        return true;
    }

    private void InvalidateConfirmations()
    {
        Revision++;
        confirmedPlayers.Clear();
        foreach (var player in players)
            if (player.Value == CalibrationGuide.Progress.Confirmed) confirmedPlayers.Add(player.Key);
        foreach (ulong player in confirmedPlayers) players[player] = CalibrationGuide.Progress.Rotation;
    }
}
