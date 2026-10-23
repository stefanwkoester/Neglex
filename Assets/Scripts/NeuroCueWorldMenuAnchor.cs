using UnityEngine;

/// <summary>
/// Keeps the main menu canvas fixed in the room (MR) instead of on the headset.
/// Places it once in front of the user when the menu is shown, facing the user at eye height.
/// </summary>
[DisallowMultipleComponent]
public class NeuroCueWorldMenuAnchor : MonoBehaviour
{
    [SerializeField] private float distanceFromHead = 1.1f;
    [SerializeField] private float verticalOffset = 0f;
    [SerializeField] private bool detachFromHeadOnAwake = true;
    [SerializeField] private Vector3 canvasScale = new Vector3(0.001f, 0.001f, 0.001f);

    public float DistanceFromHead
    {
        get => distanceFromHead;
        set => distanceFromHead = Mathf.Max(0.1f, value);
    }

    private void Awake()
    {
        if (detachFromHeadOnAwake)
            DetachFromHead();
    }

    public void DetachFromHead()
    {
        // Always unparent so the menu cannot stay head-locked under the camera rig.
        if (transform.parent != null)
            transform.SetParent(null, true);
    }

    public bool PlaceInFrontOfUser()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return false;

        Transform cameraTransform = camera.transform;

        // Wait until head tracking has a sensible height (not floor / uninitialized).
        if (cameraTransform.position.y < 0.8f)
            return false;

        DetachFromHead();

        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();

        float distance = GameplayManager.Instance != null
            ? GameplayManager.Instance.StartMenuDistanceMeters
            : distanceFromHead;
        Vector3 position = cameraTransform.position + forward * distance;
        position.y = cameraTransform.position.y + verticalOffset;

        // Match former CenterEye parenting: canvas +Z points the same way the user looks,
        // so the UI front is readable (LookRotation(-forward) shows mirrored text).
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = canvasScale;
        return true;
    }
}
