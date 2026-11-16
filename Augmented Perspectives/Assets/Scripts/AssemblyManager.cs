using TMPro;
using Unity.Netcode;
using UnityEngine;

public class AssemblyManager : NetworkBehaviour
{
    [Header("Assembly")]
    [SerializeField] private AssemblePart[] parts = new AssemblePart[0];

    [Header("Score display")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private bool showLiveScore = true;

    private bool hasReachedSuccess;

    // The server/host writes these values.
    // Every connected player receives them.
    private readonly NetworkVariable<float> networkScore = new(0f);
    private readonly NetworkVariable<bool> networkSuccess = new(false);

    public int TotalPartCount => parts == null ? 0 : parts.Length;

    public int SnappedPartCount
    {
        get
        {
            int count = 0;
            if (parts != null)
                foreach (AssemblePart part in parts)
                    if (part != null && part.IsSnapped) count++;
            return count;
        }
    }

    // Missing references must never count as snapped parts.
    public bool AllPartsSnapped => TotalPartCount > 0 && SnappedPartCount == TotalPartCount;
    public bool IsComplete => AllPartsSnapped && (!IsSpawned || networkSuccess.Value);

    public float OverallScore
    {
        get
        {
            if (parts == null || parts.Length == 0)
                return 0f;

            float total = 0f;
            int validParts = 0;

            foreach (AssemblePart part in parts)
            {
                if (part == null)
                    continue;

                total += part.Score01;
                validParts++;
            }

            return validParts == 0
                ? 0f
                : total / validParts * 100f;
        }
    }

    public override void OnNetworkSpawn()
    {
        networkScore.OnValueChanged += OnNetworkScoreChanged;
        networkSuccess.OnValueChanged += OnSuccessChanged;
        hasReachedSuccess = false;

        RefreshScoreDisplay();
    }

    public override void OnNetworkDespawn()
    {
        networkScore.OnValueChanged -= OnNetworkScoreChanged;
        networkSuccess.OnValueChanged -= OnSuccessChanged;
    }

    private void Update()
    {
        if (!CalibrationLobby.CanPlay) return;
        if (IsSpawned && IsServer)
        {
            float accuracy = OverallScore;
            if (Mathf.Abs(networkScore.Value - accuracy) >= 0.1f ||
                (AllPartsSnapped && !networkSuccess.Value))
                networkScore.Value = accuracy;
            networkSuccess.Value = AllPartsSnapped;
        }
        CheckForSuccess();
        if (showLiveScore)
            RefreshScoreDisplay();
    }

    public void RequestReset()
    {
        if (!CalibrationLobby.CanPlay) return;
        if (IsSpawned) ResetRpc();
        else ResetLooseParts();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ResetRpc()
    {
        if (!CalibrationLobby.CanPlay) return;
        ResetLooseParts();
        networkScore.Value = OverallScore;
    }

    private void ResetLooseParts()
    {
        if (parts == null) return;
        foreach (AssemblePart part in parts)
        {
            if (part == null || part.IsSnapped) continue;
            if (part.TryGetComponent(out NetworkGrabOwnership ownership) && ownership.IsSpawned)
                ownership.ResetLoosePart();
            else
                part.ReturnToStart();
        }
        RefreshScoreDisplay();
    }

    public void ResetAssembly() => RequestReset();

    private void OnSuccessChanged(bool previous, bool current)
    {
        if (!current)
        {
            hasReachedSuccess = false;
            GetComponent<AssemblySuccessFeedback>()?.ResetFeedback();
        }
        RefreshScoreDisplay();
    }

    private void OnNetworkScoreChanged(
        float previousValue,
        float newValue)
    {
        RefreshScoreDisplay();
    }

    private void CheckForSuccess()
    {
        if (!CalibrationLobby.CanPlay) return;
        if (hasReachedSuccess)
            return;
        // Wait for both the server's completion and all local snap updates. They
        // can arrive in different frames because they belong to different objects.
        if (!IsComplete)
            return;
        hasReachedSuccess = true;
        GetComponent<AssemblySuccessFeedback>()?.PlaySuccess();
        Debug.Log($"[AssemblySuccess] Completed: all {TotalPartCount} parts snapped and locked.");
        WriteScoreText();
    }

    private void RefreshScoreDisplay()
    {
        CheckForSuccess();
        WriteScoreText();
    }

    private void WriteScoreText()
    {
        if (scoreText == null)
            return;
        scoreText.text = $"Assembly accuracy: {(IsSpawned ? networkScore.Value : OverallScore):0}%";
    }
}
