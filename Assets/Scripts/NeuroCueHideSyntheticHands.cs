using UnityEngine;

/// <summary>
/// Forces Meta tracked hands to a white outline over the user's real passthrough hands.
/// Not a solid/physical skin mesh — fill stays nearly transparent so the real hand shows through.
/// </summary>
[DefaultExecutionOrder(500)]
public class NeuroCueHideSyntheticHands : MonoBehaviour
{
    [Tooltip("Meta Interaction SDK OculusHandWire material (outline on passthrough).")]
    [SerializeField] private Material outlineHandMaterial;

    [SerializeField] private Color outlineColor = Color.white;
    [Tooltip("Keep fill almost invisible so the person's real hand is visible through passthrough.")]
    [SerializeField] [Range(0f, 0.2f)] private float fillOpacity = 0.02f;
    [SerializeField] [Range(0f, 1f)] private float outlineOpacity = 1f;
    [SerializeField] private float outlineWidth = 0.0032f;
    [SerializeField] private float rescanInterval = 0.5f;

    private Material runtimeOutlineMaterial;
    private float nextRescanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<NeuroCueHideSyntheticHands>() != null)
            return;

        GameObject host = new GameObject("NeuroCueOutlineHands");
        DontDestroyOnLoad(host);
        host.AddComponent<NeuroCueHideSyntheticHands>();
    }

    private void Awake()
    {
        EnsureOutlineMaterial();
        ApplyOutlineHandLook();
    }

    private void OnDestroy()
    {
        if (runtimeOutlineMaterial != null)
            Destroy(runtimeOutlineMaterial);
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextRescanTime)
            return;

        nextRescanTime = Time.unscaledTime + rescanInterval;
        ApplyOutlineHandLook();
    }

    private void ApplyOutlineHandLook()
    {
        EnsureOutlineMaterial();
        // Meta can reset materials at runtime — retune every pass.
        ApplyOutlineTuning(runtimeOutlineMaterial);

        // Hide the Real Hands placeholder mesh (solid hands at tracking origin / floor).
        HideRealHandsPlaceholder();

        string[] trackedHandRoots =
        {
            "[BuildingBlock] Hand Tracking left",
            "[BuildingBlock] Hand Tracking right"
        };

        for (int i = 0; i < trackedHandRoots.Length; i++)
        {
            GameObject root = GameObject.Find(trackedHandRoots[i]);
            if (root == null)
                continue;

            // Keep mesh drivers on so the outline tracks the real hand pose.
            Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (int b = 0; b < behaviours.Length; b++)
            {
                Behaviour behaviour = behaviours[b];
                if (behaviour == null)
                    continue;

                string typeName = behaviour.GetType().Name;
                if (typeName == "OVRMeshRenderer" || typeName == "OVRMesh")
                {
                    if (!behaviour.enabled)
                        behaviour.enabled = true;
                }
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null)
                    continue;

                // Never leave a solid opaque hand mesh visible.
                if (!renderer.enabled)
                    renderer.enabled = true;

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                if (runtimeOutlineMaterial == null)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    renderer.sharedMaterial = runtimeOutlineMaterial;
                    continue;
                }

                bool changed = false;
                for (int m = 0; m < materials.Length; m++)
                {
                    if (materials[m] != runtimeOutlineMaterial)
                    {
                        materials[m] = runtimeOutlineMaterial;
                        changed = true;
                    }
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }
    }

    private static void HideRealHandsPlaceholder()
    {
        GameObject realHands = GameObject.Find("[BuildingBlock] Real Hands");
        if (realHands == null)
            return;

        Renderer[] placeholderRenderers = realHands.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < placeholderRenderers.Length; i++)
        {
            if (placeholderRenderers[i] != null)
                placeholderRenderers[i].enabled = false;
        }
    }

    private void EnsureOutlineMaterial()
    {
        if (runtimeOutlineMaterial != null)
            return;

        if (outlineHandMaterial == null)
            outlineHandMaterial = Resources.Load<Material>("OculusHandWire");

        if (outlineHandMaterial == null)
            outlineHandMaterial = LoadPackageHandWire();

        if (outlineHandMaterial != null)
        {
            runtimeOutlineMaterial = new Material(outlineHandMaterial);
            runtimeOutlineMaterial.name = "NeuroCueWhiteOutlineHand";
            ApplyOutlineTuning(runtimeOutlineMaterial);
            return;
        }

        // Fallback if OculusHandWire is unavailable: transparent white shell + bright edge.
        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Unlit/Color") ??
            Shader.Find("Sprites/Default");

        runtimeOutlineMaterial = new Material(shader);
        runtimeOutlineMaterial.name = "NeuroCueWhiteOutlineHandFallback";
        Color fill = new Color(1f, 1f, 1f, fillOpacity);
        if (runtimeOutlineMaterial.HasProperty("_BaseColor"))
            runtimeOutlineMaterial.SetColor("_BaseColor", fill);
        if (runtimeOutlineMaterial.HasProperty("_Color"))
            runtimeOutlineMaterial.SetColor("_Color", fill);
        if (runtimeOutlineMaterial.HasProperty("_Surface"))
            runtimeOutlineMaterial.SetFloat("_Surface", 1f);
        runtimeOutlineMaterial.renderQueue = 3000;
    }

    private static Material LoadPackageHandWire()
    {
        Material[] all = Resources.FindObjectsOfTypeAll<Material>();
        for (int i = 0; i < all.Length; i++)
        {
            Material material = all[i];
            if (material == null)
                continue;

            // Prefer wire/outline materials; never use opaque BasicHand / skin mats.
            string name = material.name;
            if (name == "OculusHandWire" || name.IndexOf("HandWire", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return material;
        }

        for (int i = 0; i < all.Length; i++)
        {
            Material material = all[i];
            if (material != null && material.name == "OculusHand")
                return material;
        }

        return null;
    }

    private void ApplyOutlineTuning(Material material)
    {
        if (material == null)
            return;

        // White outline of the tracked hand; fill nearly gone so real skin shows in passthrough.
        if (material.HasProperty("_OutlineColor"))
            material.SetColor("_OutlineColor", outlineColor);
        if (material.HasProperty("_OutlineGlowColor"))
            material.SetColor("_OutlineGlowColor", Color.white);
        if (material.HasProperty("_GlowColor"))
            material.SetColor("_GlowColor", Color.white);
        if (material.HasProperty("_ColorTop"))
            material.SetColor("_ColorTop", new Color(1f, 1f, 1f, fillOpacity));
        if (material.HasProperty("_ColorBottom"))
            material.SetColor("_ColorBottom", new Color(1f, 1f, 1f, fillOpacity));
        if (material.HasProperty("_ColorPrimary"))
            material.SetColor("_ColorPrimary", new Color(1f, 1f, 1f, fillOpacity));
        if (material.HasProperty("_ColorSecondary"))
            material.SetColor("_ColorSecondary", new Color(1f, 1f, 1f, fillOpacity));
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", new Color(1f, 1f, 1f, fillOpacity));
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(1f, 1f, 1f, fillOpacity));
        if (material.HasProperty("_Opacity"))
            material.SetFloat("_Opacity", fillOpacity);
        if (material.HasProperty("_GlobalOpacity"))
            material.SetFloat("_GlobalOpacity", fillOpacity);
        if (material.HasProperty("_TextureOpacity"))
            material.SetFloat("_TextureOpacity", 0f);
        if (material.HasProperty("_OutlineOpacity"))
            material.SetFloat("_OutlineOpacity", outlineOpacity);
        if (material.HasProperty("_OutlineIntensity"))
            material.SetFloat("_OutlineIntensity", 1.35f);
        if (material.HasProperty("_OutlineWidth"))
            material.SetFloat("_OutlineWidth", outlineWidth);
        if (material.HasProperty("_Fresnel"))
            material.SetFloat("_Fresnel", 1f);
        if (material.HasProperty("_FresnelPower"))
            material.SetFloat("_FresnelPower", 0.35f);
        if (material.HasProperty("_GenerateGlow"))
            material.SetFloat("_GenerateGlow", 0f);
    }
}
