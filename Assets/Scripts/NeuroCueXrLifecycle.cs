using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.XR.Management;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Tears down OpenXR cleanly when leaving Play Mode so Meta XR Simulator can restart
/// without requiring a full Unity Editor restart (Failed to initialize Open XR / xrSession: 0).
/// </summary>
[DefaultExecutionOrder(-10000)]
public class NeuroCueXrLifecycle : MonoBehaviour
{
    private static NeuroCueXrLifecycle instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_EDITOR
        if (instance != null)
            return;

        GameObject host = new GameObject("NeuroCueXrLifecycle");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<NeuroCueXrLifecycle>();
#endif
    }

    private void OnApplicationQuit()
    {
        ShutdownXr("OnApplicationQuit");
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        ShutdownXr("OnDestroy");
    }

    public static void ShutdownXr(string reason)
    {
#if UNITY_EDITOR
        try
        {
            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
                return;

            XRManagerSettings manager = settings.Manager;

            if (manager.isInitializationComplete || manager.activeLoader != null)
            {
                Debug.Log("NeuroCue XR: stopping subsystems (" + reason + ")");
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("NeuroCue XR: shutdown failed (" + reason + "): " + ex.Message);
        }
#endif
    }
}

#if UNITY_EDITOR
/// <summary>
/// Editor-side Play Mode hooks: deactivate leftover OpenXR state between Play sessions
/// and re-activate Meta XR Simulator safely.
/// </summary>
[InitializeOnLoad]
public static class NeuroCueXrPlayModeGuard
{
    private const string PrefKey = "NeuroCue.AutoEnableMetaXrSimulator";
    private const string ActivateMenuPath = "Meta/Meta XR Simulator/Activate";
    private const string DeactivateMenuPath = "Meta/Meta XR Simulator/Deactivate";
    private static bool pendingReactivate;

    static NeuroCueXrPlayModeGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static bool AutoEnableEnabled => EditorPrefs.GetBool(PrefKey, true);

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingPlayMode:
                // Critical: release the OpenXR session before Edit Mode resumes.
                NeuroCueXrLifecycle.ShutdownXr("ExitingPlayMode");
                pendingReactivate = AutoEnableEnabled;
                break;

            case PlayModeStateChange.EnteredEditMode:
                // Give the runtime a beat to finish teardown, then recycle simulator + XR.
                EditorApplication.delayCall += () =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        CleanupStaleSimulatorProcesses();
                        NeuroCueXrLifecycle.ShutdownXr("EnteredEditMode");

                        if (pendingReactivate)
                        {
                            pendingReactivate = false;
                            // Toggle deactivate→activate so the next Play gets a fresh session.
                            EditorApplication.ExecuteMenuItem(DeactivateMenuPath);
                            EditorApplication.delayCall += () =>
                            {
                                EditorApplication.ExecuteMenuItem(ActivateMenuPath);
                            };
                        }
                    };
                };
                break;

            case PlayModeStateChange.ExitingEditMode:
                // Ensure no stale loader is still marked active before Unity boots XR again.
                NeuroCueXrLifecycle.ShutdownXr("ExitingEditMode");
                if (AutoEnableEnabled)
                    EditorApplication.ExecuteMenuItem(ActivateMenuPath);
                break;
        }
    }

    private static void CleanupStaleSimulatorProcesses()
    {
        // Only the simulator UI process typically holds a dead OpenXR session lock.
        // Leave Synthetic Environment Server running if present.
        string[] processNames =
        {
            "MetaXrSimulator",
            "MetaXRSimulator",
            "metaxrsimulator"
        };

        for (int i = 0; i < processNames.Length; i++)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processNames[i]);
                for (int p = 0; p < processes.Length; p++)
                {
                    try
                    {
                        if (!processes[p].HasExited)
                        {
                            Debug.Log("NeuroCue XR: closing leftover process " + processes[p].ProcessName + " (" + processes[p].Id + ")");
                            processes[p].Kill();
                            processes[p].WaitForExit(2000);
                        }
                    }
                    catch
                    {
                        // Ignore access/denied races — best effort cleanup.
                    }
                    finally
                    {
                        processes[p].Dispose();
                    }
                }
            }
            catch
            {
                // Process name may not exist on this machine.
            }
        }
    }

    [MenuItem("NeuroCue/XR Simulator/Reset OpenXR Session Now", false, 3)]
    private static void ResetOpenXrSessionNow()
    {
        NeuroCueXrLifecycle.ShutdownXr("ManualReset");
        CleanupStaleSimulatorProcesses();
        EditorApplication.ExecuteMenuItem(DeactivateMenuPath);
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(ActivateMenuPath);
            EditorUtility.DisplayDialog(
                "NeuroCue XR Reset",
                "OpenXR was deinitialized and Meta XR Simulator was re-activated.\n\nYou can press Play again without restarting Unity.",
                "OK");
        };
    }
}
#endif
