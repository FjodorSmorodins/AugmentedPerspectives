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

    [Header("Success")]
    [SerializeField, Range(0f, 100f)] private float successThreshold = 85f;
    private bool hasReachedSuccess;
    private bool awaitingNetworkReset;

    // The server/host writes these values.
    // Every connected player receives them.
    private readonly NetworkVariable<float> networkScore = new(0f);
    private readonly NetworkVariable<bool> networkSubmitted = new(false);
    private readonly NetworkVariable<bool> networkSuccess = new(false);
    private bool localSubmitted;
    private float localScore;
    private bool Submitted => IsSpawned ? networkSubmitted.Value : localSubmitted;

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
        networkSubmitted.OnValueChanged += OnSubmittedChanged;
        networkSuccess.OnValueChanged += OnSuccessChanged;
        hasReachedSuccess = false;

        ApplySubmittedState(networkSubmitted.Value);
        RefreshScoreDisplay();
    }

    public override void OnNetworkDespawn()
    {
        networkScore.OnValueChanged -= OnNetworkScoreChanged;
        networkSubmitted.OnValueChanged -= OnSubmittedChanged;
        networkSuccess.OnValueChanged -= OnSuccessChanged;
    }

    private void Update()
    {
        if (IsSpawned && IsServer && !Submitted)
        {
            float accuracy = OverallScore;
            if (Mathf.Abs(networkScore.Value - accuracy) >= 0.1f ||
                (accuracy >= successThreshold && !networkSuccess.Value))
                networkScore.Value = accuracy;
            if (accuracy >= successThreshold)
                networkSuccess.Value = true;
        }
        CheckForSuccess();
        if (showLiveScore && !Submitted)
            RefreshScoreDisplay();
    }

    // Connect the Submit button to this method.
    public void RequestSubmit()
    {
        if (Submitted)
            return;
        if (!IsSpawned)
        {
            localScore = OverallScore;
            localSubmitted = true;
            ApplySubmittedState(true);
            RefreshScoreDisplay();
            return;
        }
        SubmitRpc();
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void SubmitRpc()
    {
        // This code executes on the host/server.
        if (networkSubmitted.Value)
            return;

        networkScore.Value = OverallScore;
        if (networkScore.Value >= successThreshold)
            networkSuccess.Value = true;
        networkSubmitted.Value = true;
    }

    // Connect the Reset button to this method.
    public void RequestReset()
    {
        if (!IsSpawned)
        {
            ResetParts();
            localSubmitted = false;
            localScore = 0f;
            RefreshScoreDisplay();
            return;
        }
        ResetRpc();
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void ResetRpc()
    {
        networkSuccess.Value = false;
        ResetParts();

        networkScore.Value = 0f;
        networkSuccess.Value = false;
        networkSubmitted.Value = false;
        ResetRemotePartsRpc();
    }

    [Rpc(SendTo.NotServer)]
    private void ResetRemotePartsRpc()
    {
        awaitingNetworkReset = networkSuccess.Value;
        hasReachedSuccess = false;
        GetComponent<AssemblySuccessFeedback>()?.ResetFeedback();
        foreach (AssemblePart part in parts)
            if (part != null)
                part.ResetPart();
        RefreshScoreDisplay();
    }

    private void OnSuccessChanged(bool previous, bool current)
    {
        if (!current)
        {
            awaitingNetworkReset = false;
            hasReachedSuccess = false;
            GetComponent<AssemblySuccessFeedback>()?.ResetFeedback();
        }
        RefreshScoreDisplay();
    }

    private void ResetParts()
    {
        hasReachedSuccess = false;
        GetComponent<AssemblySuccessFeedback>()?.ResetFeedback();
        foreach (AssemblePart part in parts)
        {
            if (part == null)
                continue;

            NetworkObject partNetworkObject =
                part.GetComponent<NetworkObject>();

            if (partNetworkObject != null &&
                partNetworkObject.IsSpawned &&
                partNetworkObject.OwnerClientId !=
                    NetworkManager.ServerClientId)
            {
                partNetworkObject.ChangeOwnership(
                    NetworkManager.ServerClientId);
            }

            // Reset locally in offline mode, or on the server in a network session.
            part.ResetPart();
        }
    }

    public void SubmitAssembly() => RequestSubmit();
    public void ResetAssembly() => RequestReset();

    private void OnNetworkScoreChanged(
        float previousValue,
        float newValue)
    {
        RefreshScoreDisplay();
    }

    private void OnSubmittedChanged(
        bool previousValue,
        bool newValue)
    {
        ApplySubmittedState(newValue);
        RefreshScoreDisplay();
    }

    private void ApplySubmittedState(bool isSubmitted)
    {
        foreach (AssemblePart part in parts)
        {
            if (part == null)
                continue;

            if (isSubmitted)
                part.FreezeForSubmission();
            else
                part.UnfreezeAfterSubmission();
        }
    }

    private void CheckForSuccess()
    {
        if (hasReachedSuccess || (IsSpawned && awaitingNetworkReset))
            return;
        if (IsSpawned && !networkSuccess.Value)
            return;
        float accuracy = IsSpawned ? networkScore.Value : (Submitted ? localScore : OverallScore);
        if (!IsSpawned && accuracy < successThreshold)
            return;
        hasReachedSuccess = true;
        GetComponent<AssemblySuccessFeedback>()?.PlaySuccess();
        Debug.Log($"[AssemblySuccess] Accuracy reached {accuracy:0}% (target {successThreshold:0}%).");
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
        string accuracyText = Submitted
            ? $"Final accuracy: {(IsSpawned ? networkScore.Value : localScore):0}%"
            : $"Assembly accuracy: {(IsSpawned ? networkScore.Value : OverallScore):0}%";
        scoreText.text = hasReachedSuccess
            ? $"<color=#55FF77>SUCCESS!</color>\n{accuracyText}"
            : accuracyText;
    }
}
