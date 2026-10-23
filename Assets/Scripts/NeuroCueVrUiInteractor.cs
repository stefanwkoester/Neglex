using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Quest 3 UI clicks via controller / hand rays against world-space canvases.
/// Uses OVRCameraRig anchor transforms (updated every frame) instead of fragile OVRInput-only poses.
/// </summary>
[DefaultExecutionOrder(-200)]
public class NeuroCueVrUiInteractor : MonoBehaviour
{
    public static NeuroCueVrUiInteractor Instance { get; private set; }

    [SerializeField] private float maxRayDistance = 12f;
    [SerializeField] private float pinchAxisThreshold = 0.45f;
    [SerializeField] private float pokeRadius = 0.12f;
    [Tooltip("World poke reach for Exit (meters). Touch fires Exit without needing a pinch.")]
    [SerializeField] private float exitPokeRadius = 0.28f;
    [Tooltip("Max aim angle (degrees) from hand/controller ray to Exit for a through-cue click.")]
    [SerializeField] private float exitAimAngleDegrees = 35f;
    [Tooltip("Screen-space proximity (viewport 0-1) between fingertip and Exit for pinch-to-click.")]
    [SerializeField] private float exitScreenProximity = 0.18f;
    [SerializeField] private bool logDebugOnDevice;

    private Transform leftController;
    private Transform rightController;
    private Transform leftHand;
    private Transform rightHand;
    private Transform leftIndexTip;
    private Transform rightIndexTip;

    private readonly List<GraphicRaycaster> raycasters = new List<GraphicRaycaster>();
    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
    private readonly Dictionary<int, bool> pressedByPointer = new Dictionary<int, bool>();
    private bool exitTouchLatched;
    private float nextCacheTime;
    private float nextAllowedUiClickTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapAfterSceneLoad()
    {
        NeuroCueVrUiBootstrapRunner.Ensure();
    }

    public static void EnsureExists()
    {
        NeuroCueXrCanvasBootstrap.EnsureWorldSpaceCanvases();

        if (Instance != null)
            return;

        EventSystem eventSystem = Object.FindObjectOfType<EventSystem>();
        if (eventSystem == null)
        {
            NeuroCueVrUiBootstrapRunner.Ensure();
            return;
        }

        if (eventSystem.GetComponent<NeuroCueVrUiInteractor>() == null)
            eventSystem.gameObject.AddComponent<NeuroCueVrUiInteractor>();
    }

    private void Awake()
    {
        Instance = this;
        CacheInputSources();
        NeuroCueXrCanvasBootstrap.EnsureWorldSpaceCanvases();
        RefreshRaycasters();
        NeuroCueXrCanvasBootstrap.DisablePokeAutoClick();
    }

    private void Update()
    {
        if (EventSystem.current == null)
            return;

        if (Time.unscaledTime >= nextCacheTime)
        {
            CacheInputSources();
            RefreshRaycasters();
            NeuroCueXrCanvasBootstrap.DisablePokeAutoClick();
            nextCacheTime = Time.unscaledTime + 0.5f;
        }

        // Hands/controllers aim. Do not use Game-view mouse — XR Simulator is a separate window,
        // and ScreenPointToRay misses the buttons the user is actually pointing at.
        ProcessSide(true, 0);
        ProcessSide(false, 1);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void ProcessSide(bool left, int pointerId)
    {
        if (!TryGetPointer(left, out Vector3 origin, out Vector3 direction))
        {
            pressedByPointer[pointerId] = false;
            return;
        }

        bool selectPressed = IsSelectPressed(left);
        bool wasPressed = pressedByPointer.TryGetValue(pointerId, out bool previous) && previous;
        bool selectDown = selectPressed && !wasPressed;
        pressedByPointer[pointerId] = selectPressed;

        if (!selectDown || Time.unscaledTime < nextAllowedUiClickTime)
            return;

        Vector3 rayOrigin = origin - direction.normalized * 0.45f;
        GameObject hit = RaycastUiButtonsWorld(rayOrigin, direction, out Vector2 screenPosition);
        if (hit == null)
            hit = RaycastUi(rayOrigin, direction, out screenPosition);
        if (hit == null)
            return;

        nextAllowedUiClickTime = Time.unscaledTime + 0.45f;
        ClickUiTarget(hit, screenPosition);
    }

    private void CacheInputSources()
    {
        if (leftController == null)
            leftController = FindTransform("LeftControllerAnchor");

        if (rightController == null)
            rightController = FindTransform("RightControllerAnchor");

        if (leftHand == null)
            leftHand = FindTransform("LeftHandAnchor");

        if (rightHand == null)
            rightHand = FindTransform("RightHandAnchor");

        if (leftIndexTip == null || rightIndexTip == null)
            CacheIndexTipsByName();

        if (GameplayManager.Instance != null)
        {
            if (leftHand == null && GameplayManager.Instance.LeftHandAnchor != null)
                leftHand = GameplayManager.Instance.LeftHandAnchor;

            if (rightHand == null && GameplayManager.Instance.RightHandAnchor != null)
                rightHand = GameplayManager.Instance.RightHandAnchor;
        }
    }

    private static Transform FindTransform(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found != null ? found.transform : null;
    }

    private void CacheIndexTipsByName()
    {
        Transform[] all = FindObjectsOfType<Transform>(true);
        Transform head = Camera.main != null ? Camera.main.transform : null;

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t.name != "IndexTip")
                continue;

            if (head == null)
            {
                if (leftIndexTip == null)
                    leftIndexTip = t;
                else if (rightIndexTip == null)
                    rightIndexTip = t;
                continue;
            }

            float side = head.InverseTransformPoint(t.position).x;
            if (side <= 0f && leftIndexTip == null)
                leftIndexTip = t;
            else if (side > 0f && rightIndexTip == null)
                rightIndexTip = t;
        }
    }

    /// <summary>
    /// Physical poke of Exit — no pinch required. Latched so one touch = one click.
    /// </summary>
    private bool TryTouchExit(Transform finger)
    {
        RectTransform exitRect = GetActiveExitRect();
        if (exitRect == null || finger == null || !finger.gameObject.activeInHierarchy)
        {
            if (finger == null || exitRect == null)
                exitTouchLatched = false;
            return false;
        }

        Button exitButton = exitRect.GetComponent<Button>();
        if (exitButton == null || !exitButton.isActiveAndEnabled || !exitButton.interactable)
            return false;

        Vector3 exitCenter = exitRect.TransformPoint(exitRect.rect.center);
        float distance = Vector3.Distance(finger.position, exitCenter);

        // Also accept collider bounds if present (thick Z poke volume).
        Collider exitCollider = exitRect.GetComponent<Collider>();
        bool insideCollider = exitCollider != null && exitCollider.bounds.Contains(finger.position);

        bool touching = insideCollider || distance <= exitPokeRadius;
        if (!touching)
        {
            exitTouchLatched = false;
            return false;
        }

        if (exitTouchLatched)
            return true;

        exitTouchLatched = true;
        if (logDebugOnDevice)
            Debug.Log("NeuroCue Exit touch click");

        ClickUiTarget(exitButton.gameObject, Vector2.zero);
        return true;
    }

    /// <summary>
    /// Pinch while fingertip is near Exit on screen — works at distance without perfect 3D aim.
    /// </summary>
    private GameObject RaycastExitByScreenProximity(
        Transform finger,
        Vector3 origin,
        Vector3 direction,
        out Vector2 screenPosition)
    {
        screenPosition = Vector2.zero;

        RectTransform exitRect = GetActiveExitRect();
        if (exitRect == null)
            return null;

        Button exitButton = exitRect.GetComponent<Button>();
        if (exitButton == null || !exitButton.isActiveAndEnabled || !exitButton.interactable)
            return null;

        Camera cam = Camera.main;
        if (cam == null)
            return null;

        Vector3 exitCenter = exitRect.TransformPoint(exitRect.rect.center);
        Vector3 exitViewport = cam.WorldToViewportPoint(exitCenter);
        if (exitViewport.z <= 0f)
            return null;

        // Prefer where the hand is aiming on screen; fall back to fingertip projection.
        Vector3 aimPoint = origin + direction.normalized * Mathf.Max(0.4f, Vector3.Distance(origin, exitCenter));
        Vector3 aimViewport = cam.WorldToViewportPoint(aimPoint);
        if (aimViewport.z <= 0f && finger != null)
            aimViewport = cam.WorldToViewportPoint(finger.position);

        if (aimViewport.z <= 0f)
            return null;

        float dx = aimViewport.x - exitViewport.x;
        float dy = aimViewport.y - exitViewport.y;
        if ((dx * dx + dy * dy) > exitScreenProximity * exitScreenProximity)
            return null;

        screenPosition = cam.WorldToScreenPoint(exitCenter);
        return exitButton.gameObject;
    }

    private bool TryGetPointer(bool left, out Vector3 origin, out Vector3 direction)
    {
        origin = Vector3.zero;
        direction = Vector3.forward;

        Transform controller = left ? leftController : rightController;
        Transform hand = left ? leftHand : rightHand;
        Transform indexTip = left ? leftIndexTip : rightIndexTip;

        bool useController = controller != null && controller.gameObject.activeInHierarchy &&
                             OVRInput.IsControllerConnected(left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch);

        if (useController)
        {
            origin = controller.position;
            direction = controller.forward;
            return direction.sqrMagnitude > 0.0001f;
        }

        if (indexTip != null && indexTip.gameObject.activeInHierarchy)
        {
            origin = indexTip.position;
            direction = GetHandRayDirection(indexTip, hand);
            return direction.sqrMagnitude > 0.0001f;
        }

        if (hand != null && hand.gameObject.activeInHierarchy)
        {
            origin = hand.position;
            direction = hand.forward;
            return direction.sqrMagnitude > 0.0001f;
        }

        return false;
    }

    private Vector3 GetHandRayDirection(Transform indexTip, Transform hand)
    {
        // Wrist → fingertip points more reliably than IndexTip.forward on Meta hands.
        if (hand != null)
        {
            Vector3 pointing = indexTip.position - hand.position;
            if (pointing.sqrMagnitude > 0.0004f)
                return pointing.normalized;
        }

        if (indexTip.forward.sqrMagnitude > 0.0001f)
            return indexTip.forward;

        if (hand != null && hand.forward.sqrMagnitude > 0.0001f)
            return hand.forward;

        return Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
    }

    private static RectTransform GetActiveExitRect()
    {
        GameObject exit = GetActiveExitButton();
        return exit != null ? exit.GetComponent<RectTransform>() : null;
    }

    private static GameObject GetActiveExitButton()
    {
        if (GameplayManager.Instance == null)
            return null;

        GameObject exit = GameplayManager.Instance.ExitButton;
        if (exit != null && exit.activeInHierarchy)
            return exit;

        if (GameplayManager.Instance.StartScreen != null)
        {
            Transform panel = GameplayManager.Instance.StartScreen.transform.Find("Menu Panel");
            if (panel != null)
            {
                Transform startExit = panel.Find("Exit");
                if (startExit != null && startExit.gameObject.activeInHierarchy)
                    return startExit.gameObject;
            }
        }

        if (GameplayManager.Instance.MainMenuCanvas != null)
        {
            Transform startExit = GameplayManager.Instance.MainMenuCanvas.transform.Find("Exit");
            if (startExit != null && startExit.gameObject.activeInHierarchy)
                return startExit.gameObject;
        }

        return null;
    }

    /// <summary>
    /// Hits Exit even when a gameplay cue is closer on Z (between hand and Exit).
    /// Uses the Exit rect plane only — never blocked by 3D cue colliders.
    /// </summary>
    private GameObject RaycastExitIgnoringDepth(Vector3 origin, Vector3 direction, out Vector2 screenPosition)
    {
        screenPosition = Vector2.zero;

        RectTransform exitRect = GetActiveExitRect();
        if (exitRect == null)
            return null;

        Button exitButton = exitRect.GetComponent<Button>();
        if (exitButton == null || !exitButton.isActiveAndEnabled || !exitButton.interactable)
            return null;

        Vector3 exitCenter = exitRect.TransformPoint(exitRect.rect.center);
        Vector3 toExit = exitCenter - origin;
        if (toExit.sqrMagnitude < 0.0001f)
            return null;

        float distanceToExit = toExit.magnitude;
        if (distanceToExit > maxRayDistance)
            return null;

        Vector3 aim = direction.sqrMagnitude > 0.0001f ? direction.normalized : toExit.normalized;
        float angle = Vector3.Angle(aim, toExit.normalized);
        if (angle > exitAimAngleDegrees)
            return null;

        // Intersect Exit's plane; ignore any 3D cue between origin and Exit.
        Plane exitPlane = new Plane(-exitRect.forward, exitCenter);
        Ray ray = new Ray(origin, aim);
        float planeDistance = distanceToExit;
        if (exitPlane.Raycast(ray, out float hitDistance) && hitDistance <= maxRayDistance)
            planeDistance = hitDistance;
        else if (angle > exitAimAngleDegrees * 0.55f)
            return null;

        Vector3 worldHit = ray.GetPoint(planeDistance);
        Vector3 local = exitRect.InverseTransformPoint(worldHit);
        Rect rect = exitRect.rect;

        // Expand hit box slightly so corner Exit stays easy to click behind cues.
        float padX = rect.width * 0.2f;
        float padY = rect.height * 0.2f;
        bool inside =
            local.x >= rect.xMin - padX && local.x <= rect.xMax + padX &&
            local.y >= rect.yMin - padY && local.y <= rect.yMax + padY;

        if (!inside && Vector3.Distance(worldHit, exitCenter) > exitPokeRadius * 0.55f)
            return null;

        Canvas canvas = exitRect.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.worldCamera != null
            ? canvas.worldCamera
            : Camera.main;
        if (eventCamera != null)
            screenPosition = RectTransformUtility.WorldToScreenPoint(eventCamera, worldHit);

        return exitButton.gameObject;
    }

    private static Canvas GetActiveMenuCanvas()
    {
        // Prefer the world Main Menu Canvas (Exit / Play). Never aim at the head-locked HUD.
        if (GameplayManager.Instance != null && GameplayManager.Instance.MainMenuCanvas != null)
        {
            Canvas menuCanvas = GameplayManager.Instance.MainMenuCanvas.GetComponent<Canvas>();
            if (menuCanvas != null && menuCanvas.gameObject.activeInHierarchy)
                return menuCanvas;
        }

        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace || !canvas.gameObject.activeInHierarchy)
                continue;

            if (canvas.gameObject.name == "Gameplay")
                continue;

            return canvas;
        }

        return null;
    }

    private bool IsSelectPressed(bool left)
    {
        OVRInput.Controller touch = left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        OVRInput.Controller hand = left ? OVRInput.Controller.LHand : OVRInput.Controller.RHand;
        OVRInput.Controller active = OVRInput.IsControllerConnected(touch) ? touch : hand;

        // Digital press only. Analog pinch / grip / finger-distance was auto-firing in XR Simulator
        // when the head-followed hands sat inside the menu.
        bool pressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, active) ||
                       OVRInput.Get(OVRInput.Button.One, active);
#if UNITY_EDITOR
        // XR Simulator Point & click maps to the OS mouse; aim still uses the hand ray.
#if ENABLE_INPUT_SYSTEM
        pressed = pressed || (Mouse.current != null && Mouse.current.leftButton.isPressed);
#else
        try
        {
            pressed = pressed || Input.GetMouseButton(0);
        }
        catch (System.InvalidOperationException)
        {
        }
#endif
#endif
        return pressed;
    }

    private bool IsPinching(bool left)
    {
        Transform indexTip = left ? leftIndexTip : rightIndexTip;
        if (indexTip == null)
            return false;

        Transform thumbTip = FindNamedChild(indexTip.root, "ThumbTip");
        if (thumbTip == null)
            return false;

        return Vector3.Distance(indexTip.position, thumbTip.position) <= 0.045f;
    }

    private static Transform FindNamedChild(Transform root, string childName)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
                return children[i];
        }

        return null;
    }

    private GameObject PokeNearestButton(Transform finger, bool mustBeSelecting)
    {
        // Exit uses TryTouchExit (no pinch). Other buttons still need select/pinch.
        if (!mustBeSelecting || finger == null)
            return null;

        Button nearest = null;
        float nearestDistance = float.MaxValue;
        int nearestPriority = int.MinValue;

        Button[] buttons = FindObjectsOfType<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || !button.isActiveAndEnabled || !button.interactable)
                continue;

            // Exit is handled by TryTouchExit / dedicated Exit ray paths.
            if (button.gameObject.name.IndexOf("Exit", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            // Ignore any buttons under the head-locked HUD.
            if (button.transform.root != null && button.transform.root.name == "Gameplay")
                continue;
            if (GameplayManager.Instance != null &&
                GameplayManager.Instance.GameplayScreen != null &&
                button.transform.IsChildOf(GameplayManager.Instance.GameplayScreen.transform))
                continue;

            RectTransform rect = button.GetComponent<RectTransform>();
            if (rect == null)
                continue;

            Vector3 center = rect.TransformPoint(rect.rect.center);
            float distance = Vector3.Distance(finger.position, center);
            int priority = GetUiHitPriority(button.gameObject);
            float allowedRadius = pokeRadius * 1.75f;

            if (distance > allowedRadius)
                continue;

            if (priority > nearestPriority || (priority == nearestPriority && distance < nearestDistance))
            {
                nearestDistance = distance;
                nearestPriority = priority;
                nearest = button;
            }
        }

        return nearest != null ? nearest.gameObject : null;
    }

    private void ClickUiTarget(GameObject hit, Vector2 screenPosition)
    {
        if (hit == null || EventSystem.current == null)
            return;

        // Prefer the Button (hit may be child text/image). One invoke only —
        // previously ExecuteEvents + onClick.Invoke double-fired StartGame and
        // kept resetting the session timer to 01:00.
        Button button = hit.GetComponentInParent<Button>();
        if (button != null)
        {
            if (button.interactable && button.isActiveAndEnabled)
                button.onClick.Invoke();
            return;
        }

        TMP_InputField inputField = hit.GetComponentInParent<TMP_InputField>();
        if (inputField != null && inputField.isActiveAndEnabled)
        {
            NeuroTapStartMenuController menu = Object.FindObjectOfType<NeuroTapStartMenuController>();
            if (menu != null)
                menu.OpenUsernameKeyboard();
            else
                inputField.ActivateInputField();
            return;
        }

        PointerEventData clickData = new PointerEventData(EventSystem.current)
        {
            position = screenPosition,
            button = PointerEventData.InputButton.Left
        };

        ExecuteEvents.Execute(hit, clickData, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(hit, clickData, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(hit, clickData, ExecuteEvents.pointerUpHandler);
    }

    public void RefreshRaycasters()
    {
        raycasters.Clear();
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
                continue;

            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null && raycaster.enabled)
                raycasters.Add(raycaster);
        }
    }

    private GameObject RaycastUi(Vector3 origin, Vector3 direction, out Vector2 screenPosition)
    {
        screenPosition = Vector2.zero;
        Ray ray = new Ray(origin, direction.normalized);

        GameObject bestHit = null;
        float bestDistance = float.MaxValue;
        int bestPriority = int.MinValue;

        for (int i = 0; i < raycasters.Count; i++)
        {
            GraphicRaycaster raycaster = raycasters[i];
            Canvas canvas = raycaster != null ? raycaster.GetComponent<Canvas>() : null;
            if (canvas == null || !canvas.gameObject.activeInHierarchy)
                continue;

            // Head-locked gameplay HUD is display-only and must not steal Exit clicks.
            if (canvas.gameObject.name == "Gameplay")
                continue;

            Camera eventCamera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            if (eventCamera == null)
                continue;

            Plane plane = new Plane(-canvas.transform.forward, canvas.transform.position);
            if (!plane.Raycast(ray, out float distance) || distance > maxRayDistance)
                continue;

            Vector3 worldHit = ray.GetPoint(distance);
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, worldHit);

            PointerEventData pointerData = new PointerEventData(EventSystem.current) { position = screenPoint };
            raycastResults.Clear();
            raycaster.Raycast(pointerData, raycastResults);

            if (raycastResults.Count == 0)
                continue;

            GameObject hitObject = PickBestUiHit(raycastResults);
            if (hitObject == null)
                continue;

            int priority = GetUiHitPriority(hitObject);
            if (priority > bestPriority || (priority == bestPriority && distance < bestDistance))
            {
                bestPriority = priority;
                bestDistance = distance;
                bestHit = hitObject;
                screenPosition = screenPoint;
            }
        }

        return bestHit;
    }

    /// <summary>
    /// World-space button rect test. GraphicRaycaster + WorldToScreenPoint can miss
    /// when the hand/ray origin is already on the canvas.
    /// </summary>
    private GameObject RaycastUiButtonsWorld(Vector3 origin, Vector3 direction, out Vector2 screenPosition)
    {
        screenPosition = Vector2.zero;
        if (direction.sqrMagnitude < 0.0001f)
            return null;

        Ray ray = new Ray(origin, direction.normalized);
        GameObject bestHit = null;
        float bestDistance = float.MaxValue;

        Button[] buttons = FindObjectsOfType<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || !button.isActiveAndEnabled || !button.interactable)
                continue;

            if (button.transform.root != null && button.transform.root.name == "Gameplay")
                continue;
            if (GameplayManager.Instance != null &&
                GameplayManager.Instance.GameplayScreen != null &&
                button.transform.IsChildOf(GameplayManager.Instance.GameplayScreen.transform))
                continue;

            Canvas canvas = button.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace || !canvas.gameObject.activeInHierarchy)
                continue;
            if (canvas.gameObject.name == "Gameplay")
                continue;

            RectTransform rect = button.GetComponent<RectTransform>();
            if (rect == null)
                continue;

            Vector3 center = rect.TransformPoint(rect.rect.center);
            Plane plane = new Plane(-rect.forward, center);
            if (!plane.Raycast(ray, out float distance) || distance <= 0.001f || distance > maxRayDistance)
                continue;

            Vector3 worldHit = ray.GetPoint(distance);
            Vector3 local = rect.InverseTransformPoint(worldHit);
            Rect r = rect.rect;
            if (local.x < r.xMin || local.x > r.xMax || local.y < r.yMin || local.y > r.yMax)
                continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestHit = button.gameObject;
                if (Camera.main != null)
                    screenPosition = Camera.main.WorldToScreenPoint(worldHit);
            }
        }

        TMP_InputField[] fields = FindObjectsOfType<TMP_InputField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            TMP_InputField field = fields[i];
            if (field == null || !field.isActiveAndEnabled || !field.interactable)
                continue;

            if (field.transform.root != null && field.transform.root.name == "Gameplay")
                continue;
            if (GameplayManager.Instance != null &&
                GameplayManager.Instance.GameplayScreen != null &&
                field.transform.IsChildOf(GameplayManager.Instance.GameplayScreen.transform))
                continue;

            Canvas fieldCanvas = field.GetComponentInParent<Canvas>();
            if (fieldCanvas == null || fieldCanvas.renderMode != RenderMode.WorldSpace || !fieldCanvas.gameObject.activeInHierarchy)
                continue;
            if (fieldCanvas.gameObject.name == "Gameplay")
                continue;

            RectTransform fieldRect = field.GetComponent<RectTransform>();
            if (fieldRect == null)
                continue;

            Vector3 fieldCenter = fieldRect.TransformPoint(fieldRect.rect.center);
            Plane fieldPlane = new Plane(-fieldRect.forward, fieldCenter);
            if (!fieldPlane.Raycast(ray, out float fieldDistance) || fieldDistance <= 0.001f || fieldDistance > maxRayDistance)
                continue;

            Vector3 fieldHit = ray.GetPoint(fieldDistance);
            Vector3 fieldLocal = fieldRect.InverseTransformPoint(fieldHit);
            Rect fieldBounds = fieldRect.rect;
            if (fieldLocal.x < fieldBounds.xMin || fieldLocal.x > fieldBounds.xMax ||
                fieldLocal.y < fieldBounds.yMin || fieldLocal.y > fieldBounds.yMax)
                continue;

            if (fieldDistance < bestDistance)
            {
                bestDistance = fieldDistance;
                bestHit = field.gameObject;
                if (Camera.main != null)
                    screenPosition = Camera.main.WorldToScreenPoint(fieldHit);
            }
        }

        return bestHit;
    }

    private static GameObject PickBestUiHit(List<RaycastResult> results)
    {
        GameObject best = null;
        int bestPriority = int.MinValue;

        for (int i = 0; i < results.Count; i++)
        {
            GameObject go = results[i].gameObject;
            if (go == null)
                continue;

            int priority = GetUiHitPriority(go);
            if (priority > bestPriority)
            {
                bestPriority = priority;
                best = go;
            }
        }

        return best;
    }

    private static int GetUiHitPriority(GameObject go)
    {
        if (go == null)
            return -100;

        Button button = go.GetComponentInParent<Button>();
        if (button != null && button.interactable && button.isActiveAndEnabled)
        {
            string n = button.gameObject.name;
            if (n.IndexOf("Exit", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return 100;
            if (n.IndexOf("Start", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return 95;
            if (n.IndexOf("Play", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return 90;
            return 50;
        }

        return 0;
    }
}

public static class NeuroCueXrCanvasBootstrap
{
    public static void EnsureWorldSpaceCanvases()
    {
        Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
                continue;

            GameObject go = canvas.gameObject;
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();

            GraphicRaycaster graphicRaycaster = go.GetComponent<GraphicRaycaster>();
            if (graphicRaycaster != null)
            {
                graphicRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
                graphicRaycaster.ignoreReversedGraphics = false;
            }

            if (go.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                go.AddComponent<TrackedDeviceGraphicRaycaster>();

            TrackedDeviceGraphicRaycaster tracked = go.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (tracked != null)
            {
                tracked.checkFor3DOcclusion = false;
                tracked.checkFor2DOcclusion = false;
                tracked.blockingMask = 0;
            }

            if (Camera.main != null)
                canvas.worldCamera = Camera.main;
        }
    }

    /// <summary>
    /// Head-followed simulated hands poke through a close menu and ISDK treats that as a click.
    /// Ray pointers still work; poke is what auto-started the game in the simulator.
    /// </summary>
    public static void DisablePokeAutoClick()
    {
        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null || t.name != "ISDK_PokeCanvasInteraction" || !t.gameObject.scene.IsValid())
                continue;

            if (t.gameObject.activeSelf)
                t.gameObject.SetActive(false);
        }
    }
}

public class NeuroCueVrUiBootstrapRunner : MonoBehaviour
{
    private static bool scheduled;

    public static void Ensure()
    {
        if (scheduled || NeuroCueVrUiInteractor.Instance != null)
            return;

        scheduled = true;
        GameObject bootstrap = new GameObject("NeuroCueVrUiBootstrap");
        bootstrap.hideFlags = HideFlags.HideAndDontSave;
        bootstrap.AddComponent<NeuroCueVrUiBootstrapRunner>();
    }

    private IEnumerator Start()
    {
        for (int i = 0; i < 60; i++)
        {
            NeuroCueVrUiInteractor.EnsureExists();
            if (NeuroCueVrUiInteractor.Instance != null)
                break;

            yield return null;
        }

        scheduled = false;
        Destroy(gameObject);
    }
}
