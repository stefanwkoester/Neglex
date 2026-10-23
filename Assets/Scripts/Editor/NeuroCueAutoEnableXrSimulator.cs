using UnityEditor;
using UnityEngine;

/// <summary>
/// Menu toggles for Meta XR Simulator auto-activation.
/// Play Mode OpenXR teardown/reactivation is handled by NeuroCueXrPlayModeGuard.
/// </summary>
[InitializeOnLoad]
public static class NeuroCueAutoEnableXrSimulator
{
    private const string PrefKey = "NeuroCue.AutoEnableMetaXrSimulator";
    private const string ActivateMenuPath = "Meta/Meta XR Simulator/Activate";
    private const string StatusMenuPath = "Meta/Meta XR Simulator/Status";

    static NeuroCueAutoEnableXrSimulator()
    {
        EditorApplication.delayCall += TryActivateQuiet;
    }

    private static bool AutoEnableEnabled => EditorPrefs.GetBool(PrefKey, true);

    private static void TryActivateQuiet()
    {
        if (!AutoEnableEnabled)
            return;

        // Returns false if already active, not installed, or menu unavailable — that is fine.
        EditorApplication.ExecuteMenuItem(ActivateMenuPath);
    }

    [MenuItem("NeuroCue/XR Simulator/Activate Now", false, 1)]
    private static void ActivateNow()
    {
        if (EditorApplication.ExecuteMenuItem(ActivateMenuPath))
        {
            Debug.Log("NeuroCue: Meta XR Simulator activated. Press Play to test.");
            return;
        }

        EditorUtility.DisplayDialog(
            "Meta XR Simulator",
            "Could not activate Meta XR Simulator.\n\n" +
            "1. Confirm it is installed (Meta XR Simulator app).\n" +
            "2. Open Meta → Meta XR Simulator → Status.\n" +
            "3. If it shows Inactive, use Meta → Meta XR Simulator → Activate.\n" +
            "4. Ensure the Meta XR Simulator icon next to the Play button is selected.",
            "OK");

        EditorApplication.ExecuteMenuItem(StatusMenuPath);
    }

    [MenuItem("NeuroCue/XR Simulator/Auto-Enable On Play", false, 2)]
    private static void ToggleAutoEnable()
    {
        bool next = !AutoEnableEnabled;
        EditorPrefs.SetBool(PrefKey, next);
        Menu.SetChecked("NeuroCue/XR Simulator/Auto-Enable On Play", next);
        Debug.Log("NeuroCue: Auto-enable Meta XR Simulator = " + next);

        if (next)
            TryActivateQuiet();
    }

    [MenuItem("NeuroCue/XR Simulator/Auto-Enable On Play", true)]
    private static bool ToggleAutoEnableValidate()
    {
        Menu.SetChecked("NeuroCue/XR Simulator/Auto-Enable On Play", AutoEnableEnabled);
        return true;
    }
}
