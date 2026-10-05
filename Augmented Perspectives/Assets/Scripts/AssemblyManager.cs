using TMPro;
using Unity.Netcode;
using UnityEngine;

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

    // The server/host writes these values.
    // Every connected player receives them.
    private readonly NetworkVariable<float> networkScore = new(0f);
    private readonly NetworkVariable<bool> networkSubmitted = new(false);

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

        ApplySubmittedState(networkSubmitted.Value);
        RefreshScoreDisplay();
    }

    public override void OnNetworkDespawn()
    {
        networkScore.OnValueChanged -= OnNetworkScoreChanged;
        networkSubmitted.OnValueChanged -= OnSubmittedChanged;
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (showLiveScore && !networkSubmitted.Value)
            RefreshScoreDisplay();
    }

    // Connect the Submit button to this method.
    public void RequestSubmit()
    {
        if (!IsSpawned || networkSubmitted.Value)
            return;

        SubmitRpc();
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void SubmitRpc()
    {
        // This code executes on the host/server.
        if (networkSubmitted.Value)
            return;

        networkScore.Value = OverallScore;
        networkSubmitted.Value = true;
    }

    // Connect the Reset button to this method.
    public void RequestReset()
    {
        if (!IsSpawned)
            return;

        ResetRpc();
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void ResetRpc()
    {
        // This code executes only on the host/server.
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

            // Only the server moves the parts back.
            part.ResetPart();
        }

        networkScore.Value = 0f;
        networkSubmitted.Value = false;
    }

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

    private void RefreshScoreDisplay()
    {
        if (scoreText == null)
            return;

        if (networkSubmitted.Value)
        {
            scoreText.text =
                $"Final accuracy: {networkScore.Value:0}%";
        }
        else
        {
            scoreText.text =
                $"Assembly accuracy: {OverallScore:0}%";
        }
    }
}
