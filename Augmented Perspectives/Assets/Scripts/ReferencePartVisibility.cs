using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class ReferencePartVisibility : MonoBehaviour
{
    public enum Audience
    {
        Everyone,
        HostOnly,
        JoiningPlayersOnly,
        Nobody
    }

    [Tooltip("Who can see this reference part after hosting or joining. Child visuals inherit this restriction.")]
    [SerializeField] private Audience visibleTo = Audience.Everyone;
    [Tooltip("Show this reference part before connecting and during offline play.")]
    [SerializeField] private bool visibleOffline = true;

    private Renderer[] renderers;
    private bool[] originalHidden;
    private ReferencePartVisibility[] restrictions;

    private void Awake()
    {
        // A child with its own visibility component manages its own renderers.
        // It also respects the restrictions on its parent reference part.
        var ownedRenderers = new List<Renderer>();
        foreach (Renderer visual in GetComponentsInChildren<Renderer>(true))
        {
            if (visual.GetComponentInParent<ReferencePartVisibility>(true) == this)
                ownedRenderers.Add(visual);
        }
        renderers = ownedRenderers.ToArray();
        originalHidden = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            originalHidden[i] = renderers[i].forceRenderingOff;
        restrictions = GetComponentsInParent<ReferencePartVisibility>(true);
    }

    private void LateUpdate()
    {
        NetworkManager manager = NetworkManager.Singleton;
        bool hasRole = manager != null && manager.IsListening &&
                       (manager.IsHost || manager.IsConnectedClient);
        bool isHost = hasRole && manager.IsHost;
        bool visible = IsVisible(hasRole, isHost);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].forceRenderingOff = originalHidden[i] || !visible;
        }
    }

    public bool IsVisibleToRole(bool isHost) => IsVisible(true, isHost);

    private bool IsVisible(bool hasRole, bool isHost)
    {
        if (restrictions == null)
            restrictions = GetComponentsInParent<ReferencePartVisibility>(true);
        foreach (ReferencePartVisibility restriction in restrictions)
        {
            if (restriction != null && restriction.isActiveAndEnabled &&
                !restriction.AllowsVisibility(hasRole, isHost))
                return false;
        }
        return true;
    }

    private bool AllowsVisibility(bool hasRole, bool isHost)
    {
        if (!hasRole)
            return visibleOffline;
        return visibleTo == Audience.Everyone ||
               (visibleTo == Audience.HostOnly && isHost) ||
               (visibleTo == Audience.JoiningPlayersOnly && !isHost);
    }

    private void OnDisable()
    {
        if (renderers == null)
            return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].forceRenderingOff = originalHidden[i];
        }
    }
}