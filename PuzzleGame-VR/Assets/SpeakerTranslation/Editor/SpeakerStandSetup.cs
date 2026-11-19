using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SpeakerStandSetup
{
    [MenuItem("Tools/Speaker Translation/Wire Both Stands")]
    private static void WireBoth()
    {
        Transform[] stands = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(IsStand).ToArray();
        if (stands.Length != 2)
        {
            Debug.LogWarning("Expected two speaker stands. Select the button spheres and use Wire Selected Button Spheres.");
            return;
        }

        GameObject[] buttons = new GameObject[stands.Length];
        for (int index = 0; index < stands.Length; index++)
        {
            Transform stand = stands[index];
            Transform[] candidates = stand.GetComponentsInChildren<Transform>()
                .Where(child => child != stand && child.name.IndexOf("sphere", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            if (candidates.Length != 1)
            {
                Debug.LogWarning($"Select the intended button sphere under {stand.name} and use Wire Selected Button Spheres.", stand);
                return;
            }
            buttons[index] = candidates[0].gameObject;
        }
        WireButtons(buttons);
    }

    [MenuItem("Tools/Speaker Translation/Wire Selected Button Spheres")]
    private static void WireSelected()
    {
        WireButtons(Selection.gameObjects);
    }

    private static bool IsStand(Transform candidate)
    {
        string name = candidate.name.Replace(" ", "");
        return name == "SpeakerStand" || name == "SpeakerStand(1)";
    }

    private static void WireButtons(GameObject[] buttons)
    {
        bool wired = false;
        foreach (GameObject button in buttons)
        {
            Transform stand = button.transform.parent;
            while (stand != null && !IsStand(stand))
                stand = stand.parent;
            if (stand == null)
            {
                Debug.LogWarning("Select the sphere under SpeakerStand or Speaker Stand (1).", button);
                continue;
            }
            if (button.GetComponent<SpeakerStand>() == null)
            {
                Vector3 center = stand.InverseTransformPoint(button.transform.position);
                WireButton(stand, button, center + new Vector3(0, 0.36f, -0.05f));
            }
            wired = true;
        }
        if (!wired)
            return;
        EnsureInput();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    private static void WireButton(Transform stand, GameObject button, Vector3 screenPosition)
    {
        if (button.GetComponent<Collider>() == null)
            Undo.AddComponent<SphereCollider>(button);

        var panel = new GameObject("Translation Screen", typeof(RectTransform), typeof(Canvas), typeof(Image));
        Undo.RegisterCreatedObjectUndo(panel, "Add Translation Screen");
        panel.transform.SetParent(stand, false);
        var screen = (RectTransform)panel.transform;
        screen.localPosition = screenPosition;
        screen.localScale = Vector3.one * 0.0025f;
        screen.sizeDelta = new Vector2(520, 184);
        panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Image background = panel.GetComponent<Image>();
        background.color = new Color32(15, 24, 39, 255);
        background.raycastTarget = false;

        Text caption = CreateText(screen, "English Caption", "English translation appears here",
            new Vector2(16, 40), new Vector2(-16, -12), 29, Color.white);
        Text status = CreateText(screen, "Microphone Status", "Hold the sphere to speak",
            new Vector2(16, 8), new Vector2(-16, -148), 18, new Color32(147, 215, 255, 255));

        AudioSource source = Undo.AddComponent<AudioSource>(panel);
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 3f;
        source.maxDistance = 14f;

        var component = new SerializedObject(Undo.AddComponent<SpeakerStand>(button));
        component.FindProperty("caption").objectReferenceValue = caption;
        component.FindProperty("status").objectReferenceValue = status;
        component.FindProperty("speaker").objectReferenceValue = source;
        component.ApplyModifiedProperties();
    }

    private static Text CreateText(Transform parent, string name, string content,
        Vector2 offsetMin, Vector2 offsetMax, int size, Color color)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rect = (RectTransform)label.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Text text = label.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = size;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 12;
        text.resizeTextMaxSize = size;
        text.color = color;
        text.supportRichText = false;
        text.raycastTarget = false;
        return text;
    }

    private static void EnsureInput()
    {
        if (UnityEngine.Object.FindAnyObjectByType<SpeakerStandInput>() != null)
            return;
        var input = new GameObject("Speaker Stand Input");
        Undo.RegisterCreatedObjectUndo(input, "Add Speaker Stand Input");
        var settings = new SerializedObject(Undo.AddComponent<SpeakerStandInput>(input));
        if (Camera.main != null)
            settings.FindProperty("trackingOrigin").objectReferenceValue = Camera.main.transform.parent;
        settings.ApplyModifiedProperties();
    }
}
