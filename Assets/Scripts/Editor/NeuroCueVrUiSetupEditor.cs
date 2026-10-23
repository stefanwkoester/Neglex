using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public static class NeuroCueVrUiSetupEditor
{
    [MenuItem("NeuroCue/Fix VR UI Interaction (Quest 3)")]
    public static void FixVrUiInteraction()
    {
        EventSystem eventSystem = Object.FindObjectOfType<EventSystem>();
        if (eventSystem == null)
        {
            Debug.LogError("NeuroCue: No EventSystem in scene.");
            return;
        }

        if (eventSystem.GetComponent<NeuroCueVrUiInteractor>() == null)
            eventSystem.gameObject.AddComponent<NeuroCueVrUiInteractor>();

        NeuroCueVrUiInteractor.EnsureExists();
        EditorUtility.SetDirty(eventSystem.gameObject);
        EditorSceneManager.MarkSceneDirty(eventSystem.gameObject.scene);
        Debug.Log("NeuroCue: VR UI interaction configured. Save the scene.");
    }
}
