using TMPro;
using UnityEngine;

public class AssemblyManager : MonoBehaviour
{
    [Header("Assembly")]
    [SerializeField] private AssemblePart[] parts = new AssemblePart[0];

    [Header("Score display")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private bool showLiveScore = true;

    private bool submitted;

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

            return validParts == 0 ? 0f : total / validParts * 100f;
        }
    }

    private void Start()
    {
        RefreshScoreDisplay();
    }

    private void Update()
    {
        if (showLiveScore && !submitted)
            RefreshScoreDisplay();
    }

    public void SubmitAssembly()
    {
        submitted = true;

        foreach (AssemblePart part in parts)
        {
            if (part != null)
                part.FreezeForSubmission();
        }

        RefreshScoreDisplay("Final accuracy");
    }

    public void ResetAssembly()
    {
        foreach (AssemblePart part in parts)
        {
            if (part != null)
                part.ResetPart();
        }

        submitted = false;
        RefreshScoreDisplay();
    }

    public void RefreshScoreDisplay()
    {
        RefreshScoreDisplay("Assembly accuracy");
    }

    private void RefreshScoreDisplay(string label)
    {
        if (scoreText != null)
            scoreText.text = $"{label}: {OverallScore:0}%";
    }
}
