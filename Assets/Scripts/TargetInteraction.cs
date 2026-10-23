using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using static GameplayManager;

public class TargetInteraction : MonoBehaviour
{
    [Header("Settings")]
    public float HoldDuration = 1f;
    public float LifeTime = 8f;

    [Header("Cue Type")]
    public bool RequiresHold;
    public Transform HoldRing;

    [Header("Visuals")]
    public SpriteRenderer[] CountdownSegments;
    public float InsideTouchedAlpha = 0.3f;
    public bool EnableHoldColorFeedback = false;

    [Header("Touch Detection")]
    public float TapDelay = 0.1f;
    public float TouchGraceDuration = 0.3f;
    public bool UseOverlapFallback = true;
    public float TouchDetectionRadius = 0.22f;
    public LayerMask TouchDetectionLayers = ~0;
    public bool UseHandAnchorFallback = true;
    public float HandAnchorTouchRadius = 0.22f;
    public float HandAnchorExitRadius = 0.28f;
    public bool TreatAnchorFallbackAsIndex = true;
    public bool IgnoreControllerColliders = true;
    public float WrongTouchRepeatDelay = 0.5f;

    [Header("Movement Metrics")]
    public float MovementStartThreshold = 0.03f;

    private bool touching;
    private float holdTimer;
    private float timer;
    private bool completed;
    private bool finalResultLogged;
    private bool initialized;
    private bool anchorFallbackTouching;
    private float wrongTouchCooldown;
    private float lostTrackingTimer;
    private bool waitingForReacquire;

    public bool IsFinished => completed || finalResultLogged;
    public float CurrentLife => timer;

    private readonly HashSet<Collider> activeCorrectColliders = new HashSet<Collider>();
    private readonly Dictionary<SpriteRenderer, Color> originalInnerColors = new Dictionary<SpriteRenderer, Color>();

    private int cueSequence;
    private int cueNumber;
    private int cueLevel;
    private int requiredArmSide;
    private int cueHemispace;
    private bool crossesMidline;
    private int assessmentRepetitionNumber;
    private int assessmentGridRow;
    private int assessmentGridColumn;
    private int randomizationOrder;
    private string requiredHandLabel;
    private string cueHemispaceLabel;
    private string horizontalZone;
    private string verticalZone;
    private string depthZone;
    private string cueConditionId;
    private float intendedOffsetXM = float.NaN;
    private float intendedOffsetYM = float.NaN;
    private float intendedDepthM = float.NaN;
    private float levelCrossingProbability = float.NaN;
    private float levelSideOffsetM = float.NaN;
    private string sessionId;
    private string cueStartUtc;
    private float cueStartGameTime;

    private float anyArmReactionTime = float.NaN;
    private float correctArmReactionTime = float.NaN;
    private float movementStartTime = float.NaN;
    private float engagementTime = float.NaN;

    private int wrongAttempts;
    private int firstAnyArmSide = -1;
    private ContactRegion engagementRegion = ContactRegion.None;

    private Vector3 cueCenterWorld;
    private float cueRadius = float.NaN;
    private Vector3 cueViewportPosition;
    private Vector3 cueScreenPosition;

    private Vector3 firstAnyTouchWorld = MissingVector();
    private Vector3 firstCorrectTouchWorld = MissingVector();
    private Vector3 completionTouchWorld = MissingVector();
    private Vector3 initialRequiredHandPosition = MissingVector();
    private Vector3 movementStartWorld = MissingVector();

    private Transform trackedFinger;
    private bool hasLastTrackedFingerPosition;
    private Vector3 lastTrackedFingerPosition;
    private float actualPathLength;
    private float pathLengthToFirstTouch;
    private float peakHandSpeed;
    private float speedSampleSum;
    private int speedSampleCount;
    private float timeToPeakSpeed = float.NaN;
    private int directionChangeCount;
    private bool hasLastMovementDirection;
    private Vector3 lastMovementDirection;
    private int handTrackingLostCount;
    private float handTrackingLostDuration;
    private float headRotationYAtCueStart = float.NaN;
    private float headRotationYAtTouch = float.NaN;
    private bool cueVisibleInViewportAtSpawn;

    private float holdDriftSum;
    private int holdDriftSamples;
    private readonly List<KinematicSample> kinematicSamples = new List<KinematicSample>(256);
    private float nextKinematicSampleTime;

    private struct KinematicSample
    {
        public float Time;
        public bool RequiredValid;
        public bool ContralateralValid;
        public bool HeadValid;
        public bool RequiredContact;
        public bool ContralateralContact;
        public Vector3 RequiredWorld;
        public Vector3 ContralateralWorld;
        public Vector3 HeadWorld;
        public Vector3 HeadEuler;
        public Vector3 RequiredVelocity;
        public Vector3 ContralateralVelocity;
        public float RequiredSpeed;
        public float ContralateralSpeed;
    }

    public void InitializeCue(
        int sequence,
        int number,
        int level,
        int armSide,
        int hemispace,
        bool crossMidline,
        int assessmentRepetition,
        int gridRow,
        int gridColumn,
        int presentationOrder,
        string handLabel,
        string hemispaceLabel,
        string horizontalZoneLabel,
        string verticalZoneLabel,
        string depthZoneLabel,
        float intendedOffsetX,
        float intendedOffsetY,
        float intendedDepth,
        float crossingProbability,
        float sideOffsetMeters,
        string conditionId,
        bool requiresHold,
        float holdDuration,
        float lifeTime,
        string activeSessionId)
    {
        initialized = true;
        cueSequence = sequence;
        cueNumber = number;
        cueLevel = level;
        requiredArmSide = armSide;
        cueHemispace = hemispace;
        crossesMidline = crossMidline;
        assessmentRepetitionNumber = assessmentRepetition;
        assessmentGridRow = gridRow;
        assessmentGridColumn = gridColumn;
        randomizationOrder = presentationOrder;
        requiredHandLabel = handLabel;
        cueHemispaceLabel = hemispaceLabel;
        horizontalZone = horizontalZoneLabel;
        verticalZone = verticalZoneLabel;
        depthZone = depthZoneLabel;
        intendedOffsetXM = intendedOffsetX;
        intendedOffsetYM = intendedOffsetY;
        intendedDepthM = intendedDepth;
        levelCrossingProbability = crossingProbability;
        levelSideOffsetM = sideOffsetMeters;
        cueConditionId = conditionId;
        RequiresHold = requiresHold;
        EnableHoldColorFeedback = requiresHold;
        HoldDuration = holdDuration;
        LifeTime = lifeTime;
        sessionId = activeSessionId;

        cueStartUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        cueStartGameTime = Time.time;

        cueCenterWorld = GetCueCenter();
        cueRadius = GetCueRadius();

        Transform requiredAnchor = GetRequiredAnchor();
        if (requiredAnchor != null)
        {
            initialRequiredHandPosition = requiredAnchor.position;
            lastTrackedFingerPosition = requiredAnchor.position;
            hasLastTrackedFingerPosition = true;
        }

        if (Camera.main != null)
        {
            cueViewportPosition = Camera.main.WorldToViewportPoint(cueCenterWorld);
            cueScreenPosition = Camera.main.WorldToScreenPoint(cueCenterWorld);
            headRotationYAtCueStart = Camera.main.transform.eulerAngles.y;
            cueVisibleInViewportAtSpawn =
                cueViewportPosition.z > 0f &&
                cueViewportPosition.x >= 0f &&
                cueViewportPosition.x <= 1f &&
                cueViewportPosition.y >= 0f &&
                cueViewportPosition.y <= 1f;
        }

        CacheInnerRenderers();
        InitializeVisuals();
    }

    private void Start()
    {
        if (!initialized)
        {
            InitializeCue(
                0,
                0,
                GameplayManager.Instance != null ? GameplayManager.Instance.CurrentLevel : 1,
                GameplayManager.Instance != null && GameplayManager.Instance.RequireLeftHand ? 0 : 1,
                GameplayManager.Instance != null && GameplayManager.Instance.RequireLeftHand ? 0 : 1,
                false,
                GameplayManager.Instance != null ? GameplayManager.Instance.AssessmentRepetitionNumber : 0,
                GameplayManager.Instance != null ? GameplayManager.Instance.AssessmentGridRow : 0,
                GameplayManager.Instance != null ? GameplayManager.Instance.AssessmentGridColumn : 0,
                0,
                GameplayManager.Instance != null && GameplayManager.Instance.RequireLeftHand ? "left" : "right",
                GameplayManager.Instance != null && GameplayManager.Instance.RequireLeftHand ? "Left" : "Right",
                GameplayManager.Instance != null && GameplayManager.Instance.RequireLeftHand ? "left" : "right",
                "middle",
                "normal",
                0f,
                0f,
                GameplayManager.Instance != null ? GameplayManager.Instance.CueDistanceFromHeadset : float.NaN,
                float.NaN,
                0f,
                "uninitialized",
                RequiresHold,
                HoldDuration,
                LifeTime,
                "");
        }
    }

    private void Update()
    {
        if (completed)
            return;

        if (wrongTouchCooldown > 0f)
            wrongTouchCooldown -= Time.deltaTime;

        CaptureKinematicSample();
        TrackMovementPath();
        PollTouchFallbacks();

        bool pauseCueCountdown = RequiresHold && touching;

        UpdateCountdownVisuals();

        if (!pauseCueCountdown)
            timer += Time.deltaTime;

        if (timer >= LifeTime)
        {
            ExpireCue();
            return;
        }

        if (!RequiresHold)
        {
            if (touching)
            {
                holdTimer += Time.deltaTime;

                if (holdTimer >= TapDelay && !completed)
                {
                    completed = true;
                    StartCoroutine(CompleteTarget(false));
                }
            }

            return;
        }

        if (!touching)
        {
            ResetHoldVisual();
            return;
        }

        holdTimer += Time.deltaTime;
        TrackHoldDrift();

        float holdPercent = holdTimer / HoldDuration;
        UpdateHoldVisual(holdPercent);

        if (holdTimer >= HoldDuration && !completed)
        {
            completed = true;
            StartCoroutine(CompleteTarget(true));
        }
    }

    private IEnumerator CompleteTarget(bool wasHoldCue)
    {
        completed = true;

        int points = GameplayManager.Instance.GetCurrentPoints();

        if (wasHoldCue)
            points *= 2;

        completionTouchWorld =
            trackedFinger != null
                ? trackedFinger.position
                : firstCorrectTouchWorld;

        ReportCueResult(true, wasHoldCue ? "hold_complete" : "tap_complete", "final");

        GameplayManager.Instance.CorrectHit(points);

        yield return null;

        GameplayManager.Instance.SpawnTargetAfterDelay(GameplayManager.Instance.BetweenCuePause);

        Destroy(gameObject);
    }

    private void ExpireCue()
    {
        completed = true;

        ReportCueResult(false, "expired", "final");

        if (RequiresHold && GameplayManager.Instance != null)
            GameplayManager.Instance.MarkHoldCueMissed();

        GameplayManager.Instance.SpawnTargetAfterDelay(GameplayManager.Instance.BetweenCuePause);

        Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        ProcessTriggerContact(other);
    }

    private void OnTriggerStay(Collider other)
    {
        ProcessTriggerContact(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (completed)
            return;

        int handSide = ResolveHandSide(other.transform);

        if (handSide != requiredArmSide)
            return;

        activeCorrectColliders.Remove(other);

        if (activeCorrectColliders.Count > 0 || anchorFallbackTouching)
            return;

        EndTouchState();
    }

    private void ProcessTriggerContact(Collider other)
    {
        if (GameplayManager.Instance == null ||
            GameplayManager.Instance.SpawnProtectionActive ||
            completed)
        {
            return;
        }

        int handSide = ResolveHandSide(other.transform);

        if (handSide < 0)
            return;

        if (!IsHandTouchCollider(other.transform))
            return;

        ContactRegion region = GetContactRegion(other.transform);

        RecordAnyArmReaction(handSide, other.transform.position);

        if (!IsValidPrecision(region))
        {
            RegisterWrongTouch();
            return;
        }

        if (handSide != requiredArmSide)
        {
            RegisterWrongTouch();
            return;
        }

        activeCorrectColliders.Add(other);

        if (touching)
            return;

        BeginCorrectTouch(other.transform, region);
    }

    private void PollTouchFallbacks()
    {
        if (GameplayManager.Instance == null ||
            GameplayManager.Instance.SpawnProtectionActive ||
            completed)
        {
            return;
        }

        bool foundCorrectTouch = false;

        if (UseOverlapFallback)
        {
            Vector3 cueCenter = GetCueCenter();
            float radius = GetRuntimeTouchRadius();

            Collider[] hits = Physics.OverlapSphere(
                cueCenter,
                radius,
                TouchDetectionLayers,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];

                if (hit == null ||
                    hit.transform == transform ||
                    hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                int handSide = ResolveHandSide(hit.transform);

                if (handSide < 0)
                    continue;

                if (!IsHandTouchCollider(hit.transform))
                    continue;

                if (handSide == requiredArmSide)
                    foundCorrectTouch = true;

                ProcessTriggerContact(hit);
            }
        }

        if (!foundCorrectTouch &&
            touching &&
            activeCorrectColliders.Count == 0)
        {
            EndTouchState();
        }
    }

    private bool PollHandAnchorFallback()
    {
        anchorFallbackTouching = false;
        return false;
    }

    private void RegisterWrongTouch()
    {
        if (wrongTouchCooldown > 0f)
            return;

        wrongTouchCooldown = WrongTouchRepeatDelay;
        wrongAttempts++;
        GameplayManager.Instance.WrongHit();
    }

    private void CaptureKinematicSample(bool force = false)
    {
        float sampleInterval = 1f / Mathf.Max(1f, NeuroTapKinematicConfig.TargetTrajectorySampleRateHz);
        if (!force && Time.time < nextKinematicSampleTime && kinematicSamples.Count > 0)
            return;

        nextKinematicSampleTime = Time.time + sampleInterval;

        Transform required = GetRequiredAnchor();
        Transform contralateral = GetWrongAnchor();
        Camera camera = Camera.main;

        KinematicSample sample = new KinematicSample
        {
            Time = Time.time,
            RequiredValid = required != null,
            ContralateralValid = contralateral != null,
            HeadValid = camera != null,
            RequiredContact = touching,
            ContralateralContact = !float.IsNaN(anyArmReactionTime) && firstAnyArmSide >= 0 && firstAnyArmSide != requiredArmSide
        };

        if (sample.RequiredValid)
            sample.RequiredWorld = required.position;
        else
            sample.RequiredWorld = MissingVector();

        if (sample.ContralateralValid)
            sample.ContralateralWorld = contralateral.position;
        else
            sample.ContralateralWorld = MissingVector();

        if (sample.HeadValid)
        {
            sample.HeadWorld = camera.transform.position;
            sample.HeadEuler = camera.transform.eulerAngles;
        }
        else
        {
            sample.HeadWorld = MissingVector();
            sample.HeadEuler = MissingVector();
        }

        if (kinematicSamples.Count > 0)
        {
            KinematicSample previous = kinematicSamples[kinematicSamples.Count - 1];
            float dt = Mathf.Max(0.0001f, sample.Time - previous.Time);

            if (sample.RequiredValid && previous.RequiredValid)
            {
                sample.RequiredVelocity = (sample.RequiredWorld - previous.RequiredWorld) / dt;
                sample.RequiredSpeed = sample.RequiredVelocity.magnitude;
            }

            if (sample.ContralateralValid && previous.ContralateralValid)
            {
                sample.ContralateralVelocity = (sample.ContralateralWorld - previous.ContralateralWorld) / dt;
                sample.ContralateralSpeed = sample.ContralateralVelocity.magnitude;
            }
        }

        kinematicSamples.Add(sample);
    }

    private int ResolveHandSide(Transform source)
    {
        Transform current = source;

        while (current != null)
        {
            string n = current.name.ToLowerInvariant();

            if (n == "leftinteractions" ||
                n.Contains("leftinteractions") ||
                n.Contains("lefthand") ||
                n.Contains("left_hand") ||
                n.Contains("handleft") ||
                (n.Contains("left") && (n.Contains("hand") || n.Contains("finger") || n.Contains("pinch") || n.Contains("palm") || n.Contains("wrist"))))
            {
                return 0;
            }

            if (n == "rightinteractions" ||
                n.Contains("rightinteractions") ||
                n.Contains("righthand") ||
                n.Contains("right_hand") ||
                n.Contains("handright") ||
                (n.Contains("right") && (n.Contains("hand") || n.Contains("finger") || n.Contains("pinch") || n.Contains("palm") || n.Contains("wrist"))))
            {
                return 1;
            }

            current = current.parent;
        }

        return -1;
    }
    private bool IsHandTouchCollider(Transform source)
    {
        bool hasHandRoot = false;
        bool hasControllerOnlyName = false;

        Transform current = source;

        while (current != null)
        {
            string n = current.name.ToLowerInvariant();

            if (n == "leftinteractions" ||
                n == "rightinteractions" ||
                n.Contains("leftinteractions") ||
                n.Contains("rightinteractions") ||
                n.Contains("hand") ||
                n.Contains("pinch") ||
                n.Contains("index") ||
                n.Contains("finger") ||
                n.Contains("palm") ||
                n.Contains("wrist"))
            {
                hasHandRoot = true;
            }

            if (n.Contains("controller") ||
                n.Contains("remote") ||
                n.Contains("ray"))
            {
                hasControllerOnlyName = true;
            }

            current = current.parent;
        }

        if (hasHandRoot)
            return true;

        return !hasControllerOnlyName;
    }   
    private bool IsControllerLike(Transform source)
    {
        Transform current = source;

        while (current != null)
        {
            string n = current.name.ToLowerInvariant();

            bool looksLikeController =
                n.Contains("controller") ||
                n.Contains("remote") ||
                n.Contains("ray") ||
                n.Contains("interactor");

            bool looksLikeHand =
                n.Contains("hand") ||
                n.Contains("finger") ||
                n.Contains("index") ||
                n.Contains("pinch");

            if (looksLikeController && !looksLikeHand)
                return true;

            current = current.parent;
        }

        return false;
    }

    private ContactRegion GetContactRegion(Transform source)
    {
        Transform current = source;

        while (current != null)
        {
            string n = current.name.ToLowerInvariant();

            if (n.Contains("pinchpointrange") ||
                n.Contains("index") ||
                n.Contains("finger"))
            {
                return ContactRegion.ThumbOrIndex;
            }

            if (n.Contains("pincharea") ||
                n.Contains("hand") ||
                n.Contains("palm") ||
                n.Contains("wrist"))
            {
                return ContactRegion.Hand;
            }

            current = current.parent;
        }

        return ContactRegion.Limb;
    }

    private bool IsValidPrecision(ContactRegion region)
    {
        switch (GameplayManager.Instance.CurrentPrecision)
        {
            case PrecisionMode.Low:
                return true;
            case PrecisionMode.Medium:
                return region == ContactRegion.Hand || region == ContactRegion.ThumbOrIndex;
            case PrecisionMode.High:
                return region == ContactRegion.ThumbOrIndex;
        }

        return false;
    }

    private void BeginCorrectTouch(Transform touchTransform, ContactRegion region)
    {
        touching = true;
        waitingForReacquire = false;
        lostTrackingTimer = 0f;
        holdTimer = 0f;

        SetInnerTargetAlpha(true);

        trackedFinger = touchTransform;
        lastTrackedFingerPosition = trackedFinger.position;
        hasLastTrackedFingerPosition = true;

        if (float.IsNaN(correctArmReactionTime))
            correctArmReactionTime = Time.time - cueStartGameTime;

        if (float.IsNaN(engagementTime))
            engagementTime = Time.time - cueStartGameTime;

        if (Camera.main != null)
            headRotationYAtTouch = Camera.main.transform.eulerAngles.y;

        engagementRegion = region;
        firstCorrectTouchWorld = touchTransform.position;

        if (float.IsNaN(pathLengthToFirstTouch))
            pathLengthToFirstTouch = actualPathLength;
    }

    private void EndTouchState()
    {
        touching = false;
        waitingForReacquire = false;
        lostTrackingTimer = 0f;
        holdTimer = 0f;
        activeCorrectColliders.Clear();
        hasLastTrackedFingerPosition = false;
        trackedFinger = null;

        SetInnerTargetAlpha(false);
        ResetHoldVisual();

        if (GameplayManager.Instance != null)
            GameplayManager.Instance.RefreshCurrentCueInstruction();
    }

    private void RecordAnyArmReaction(int handSide, Vector3 touchWorldPosition)
    {
        if (!float.IsNaN(anyArmReactionTime))
            return;

        anyArmReactionTime = Time.time - cueStartGameTime;
        firstAnyArmSide = handSide;
        firstAnyTouchWorld = touchWorldPosition;
    }

    private void TrackMovementPath()
    {
        Transform hand = GetRequiredAnchor();

        if (trackedFinger != null)
            hand = trackedFinger;

        if (hand == null)
        {
            handTrackingLostDuration += Time.deltaTime;
            if (!waitingForReacquire)
            {
                handTrackingLostCount++;
                waitingForReacquire = true;
            }
            return;
        }

        waitingForReacquire = false;

        Vector3 currentPosition = hand.position;

        if (float.IsNaN(initialRequiredHandPosition.x))
        {
            initialRequiredHandPosition = currentPosition;
            lastTrackedFingerPosition = currentPosition;
            hasLastTrackedFingerPosition = true;
            return;
        }

        if (hasLastTrackedFingerPosition)
        {
            Vector3 movement = currentPosition - lastTrackedFingerPosition;
            float distance = movement.magnitude;
            actualPathLength += distance;

            if (Time.deltaTime > 0f && distance > 0.0001f)
            {
                float speed = distance / Time.deltaTime;
                speedSampleSum += speed;
                speedSampleCount++;

                if (speed > peakHandSpeed)
                {
                    peakHandSpeed = speed;
                    timeToPeakSpeed = Time.time - cueStartGameTime;
                }

                Vector3 direction = movement.normalized;
                if (hasLastMovementDirection &&
                    Vector3.Angle(lastMovementDirection, direction) >= 45f)
                {
                    directionChangeCount++;
                }

                lastMovementDirection = direction;
                hasLastMovementDirection = true;
            }
        }

        lastTrackedFingerPosition = currentPosition;
        hasLastTrackedFingerPosition = true;

        if (float.IsNaN(movementStartTime) &&
            Vector3.Distance(initialRequiredHandPosition, currentPosition) >= MovementStartThreshold)
        {
            movementStartTime = Time.time - cueStartGameTime;
            movementStartWorld = currentPosition;
        }
    }

    private void TrackHoldDrift()
    {
        if (trackedFinger == null)
            return;

        holdDriftSum += Vector3.Distance(cueCenterWorld, trackedFinger.position);
        holdDriftSamples++;
    }

    private void UpdateCountdownVisuals()
    {
        if (CountdownSegments == null)
            return;

        int secondsPassed = Mathf.FloorToInt(timer);

        for (int i = 0; i < CountdownSegments.Length; i++)
        {
            SpriteRenderer segment = CountdownSegments[i];

            if (segment == null)
                continue;

            Color color = Color.white;
            color.a = i < secondsPassed ? 0.2f : 1f;
            segment.color = color;
        }
    }

    private void UpdateHoldVisual(float holdPercent)
    {
        if (HoldRing == null)
            return;

        HoldRing.localScale = Vector3.one * (Mathf.Clamp01(holdPercent) * 2f);

        SpriteRenderer sr = HoldRing.GetComponent<SpriteRenderer>();

        if (sr == null)
            return;

        if (!EnableHoldColorFeedback)
        {
            sr.color = Color.white;
            return;
        }

        sr.color = holdTimer < 1f ? Color.yellow : Color.green;
    }

    private void ResetHoldVisual()
    {
        if (HoldRing == null)
            return;

        HoldRing.localScale = Vector3.zero;

        SpriteRenderer sr = HoldRing.GetComponent<SpriteRenderer>();

        if (sr != null)
            sr.color = Color.white;
    }

    private void InitializeVisuals()
    {
        ResetHoldVisual();
        SetInnerTargetAlpha(false);
        UpdateCountdownVisuals();
    }

    private void CacheInnerRenderers()
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);

        foreach (SpriteRenderer spriteRenderer in renderers)
        {
            if (spriteRenderer == null)
                continue;

            if (IsCountdownSegment(spriteRenderer))
                continue;

            if (HoldRing != null && spriteRenderer.transform == HoldRing)
                continue;

            if (!originalInnerColors.ContainsKey(spriteRenderer))
                originalInnerColors.Add(spriteRenderer, spriteRenderer.color);
        }
    }

    private bool IsCountdownSegment(SpriteRenderer spriteRenderer)
    {
        if (CountdownSegments == null)
            return false;

        foreach (SpriteRenderer segment in CountdownSegments)
        {
            if (segment == spriteRenderer)
                return true;
        }

        return false;
    }

    private void SetInnerTargetAlpha(bool touched)
    {
        foreach (KeyValuePair<SpriteRenderer, Color> pair in originalInnerColors)
        {
            if (pair.Key == null)
                continue;

            Color color = pair.Value;
            color.a = touched ? pair.Value.a * InsideTouchedAlpha : pair.Value.a;
            pair.Key.color = color;
        }
    }

    private void ReportCueResult(bool tapped, string reason, string eventType)
    {
        if (eventType == "final" && finalResultLogged)
            return;

        if (eventType == "final")
            finalResultLogged = true;

        CaptureKinematicSample(force: true);

        if (float.IsNaN(completionTouchWorld.x))
            completionTouchWorld = trackedFinger != null ? trackedFinger.position : firstCorrectTouchWorld;

        float distanceFromCenter = DistanceFromCueCenter(completionTouchWorld);
        float holdAverageDrift = holdDriftSamples > 0 ? holdDriftSum / holdDriftSamples : float.NaN;
        float holdStability = ClampMetric(1f - SafeDivide(holdAverageDrift, cueRadius));

        Vector3 pathStart = !float.IsNaN(movementStartWorld.x) ? movementStartWorld : initialRequiredHandPosition;
        float idealPathLength = !float.IsNaN(pathStart.x) ? Vector3.Distance(pathStart, cueCenterWorld) : float.NaN;

        float movementPath = pathLengthToFirstTouch > 0f ? pathLengthToFirstTouch : actualPathLength;
        float pathEfficiency = ClampMetric(SafeDivide(idealPathLength, movementPath));
        float accuracy = ClampMetric(1f - SafeDivide(distanceFromCenter, cueRadius));
        float reactionBase = !float.IsNaN(movementStartTime) ? movementStartTime : correctArmReactionTime;
        float reactionScore = ClampMetric(1f - SafeDivide(reactionBase, LifeTime));
        const float hearWeightTotal = 0.90f;
        float rawHearScore = (0.10f * holdStability) + (0.30f * pathEfficiency) + (0.20f * accuracy) + (0.30f * reactionScore);
        float coordinationScore = 100f * ClampMetric(SafeDivide(rawHearScore, hearWeightTotal));
        float cueEndGameTime = Time.time;
        float movementDuration = !float.IsNaN(movementStartTime) ? Mathf.Max(0f, cueEndGameTime - (cueStartGameTime + movementStartTime)) : float.NaN;
        float averageHandSpeed = !float.IsNaN(movementDuration) && movementDuration > 0.0001f
            ? SafeDivide(movementPath, movementDuration)
            : speedSampleCount > 0 ? speedSampleSum / speedSampleCount : float.NaN;
        float pathSmoothness = ClampMetric(SafeDivide(pathEfficiency, 1f + (0.1f * directionChangeCount)));
        float overshootDistance = !float.IsNaN(idealPathLength) ? Mathf.Max(0f, movementPath - idealPathLength) : float.NaN;
        float undershootDistance = !float.IsNaN(idealPathLength) ? Mathf.Max(0f, idealPathLength - movementPath) : float.NaN;
        Vector2 endpointError = GetEndpointError();
        float headDelta = (!float.IsNaN(headRotationYAtCueStart) && !float.IsNaN(headRotationYAtTouch))
            ? Mathf.Abs(Mathf.DeltaAngle(headRotationYAtCueStart, headRotationYAtTouch))
            : float.NaN;
        KinematicSummary kinematics = BuildKinematicSummary(cueEndGameTime, movementPath, tapped);

        GameplayManager.CueResult result = new GameplayManager.CueResult
        {
            SessionId = sessionId,
            EventType = eventType,
            CueSequence = cueSequence,
            CueNumber = cueNumber,
            Level = cueLevel,
            ParticipantId = GameplayManager.Instance != null ? GameplayManager.Instance.ParticipantId : "",
            SessionNumber = GameplayManager.Instance != null ? GameplayManager.Instance.SessionNumber : 0,
            DaysSinceStrokeOrInjury = GameplayManager.Instance != null ? GameplayManager.Instance.DaysSinceStrokeOrInjury : "",
            AffectedBodySide = GameplayManager.Instance != null ? GameplayManager.Instance.AffectedBodySide : "",
            LesionSide = GameplayManager.Instance != null ? GameplayManager.Instance.LesionSide : "",
            SuspectedNeglectSide = GameplayManager.Instance != null ? GameplayManager.Instance.SuspectedNeglectSide : "",
            DominantHand = GameplayManager.Instance != null ? GameplayManager.Instance.DominantHand : "",
            AssessmentOrTrainingMode = GameplayManager.Instance != null ? GameplayManager.Instance.AssessmentOrTrainingMode : "assessment",
            AssessmentProtocolVersion = GameplayManager.Instance != null ? GameplayManager.Instance.AssessmentProtocolVersion : "",
            AppVersion = GameplayManager.Instance != null && !string.IsNullOrEmpty(GameplayManager.Instance.AppVersionOverride)
                ? GameplayManager.Instance.AppVersionOverride
                : Application.version,
            HeadsetModel = GameplayManager.Instance != null ? GameplayManager.Instance.HeadsetModel : "",
            IsPracticeTrial = GameplayManager.Instance != null && GameplayManager.Instance.IsPracticeTrial ? 1 : 0,
            AssessmentRepetitionNumber = assessmentRepetitionNumber,
            AssessmentGridRow = assessmentGridRow,
            AssessmentGridColumn = assessmentGridColumn,
            RandomizationOrder = randomizationOrder,
            CueConditionId = cueConditionId,
            ArmSide = requiredArmSide,
            RequiredHandLabel = requiredHandLabel,
            CueHemispace = cueHemispace,
            CueHemispaceLabel = cueHemispaceLabel,
            CrossMidline = crossesMidline ? 1 : 0,
            HorizontalZone = horizontalZone,
            VerticalZone = verticalZone,
            DepthZone = depthZone,
            IntendedOffsetXM = intendedOffsetXM,
            IntendedOffsetYM = intendedOffsetYM,
            IntendedDepthM = intendedDepthM,
            LevelCrossingProbability = levelCrossingProbability,
            LevelSideOffsetM = levelSideOffsetM,
            Tapped = tapped ? 1 : 0,
            RequiresHold = RequiresHold ? 1 : 0,
            CompletionReason = reason,
            CueStartUtc = cueStartUtc,
            CueEndUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            CueStartGameTime = cueStartGameTime,
            CueEndGameTime = cueEndGameTime,
            AnyArmReactionTime = anyArmReactionTime,
            CorrectArmReactionTime = correctArmReactionTime,
            MovementStartTime = movementStartTime,
            EngagementTime = engagementTime,
            SecondsHeld = holdTimer,
            WrongAttempts = wrongAttempts,
            CueWorldPosition = cueCenterWorld,
            CueViewportPosition = new Vector2(cueViewportPosition.x, cueViewportPosition.y),
            CueScreenPosition = new Vector2(cueScreenPosition.x, cueScreenPosition.y),
            OptimalIndexWorldPosition = cueCenterWorld,
            FirstAnyArmSide = firstAnyArmSide == 0 ? "left" : firstAnyArmSide == 1 ? "right" : "",
            FirstAnyTouchWorld = firstAnyTouchWorld,
            FirstCorrectTouchWorld = firstCorrectTouchWorld,
            CompletionTouchWorld = completionTouchWorld,
            ContactRegion = engagementRegion.ToString(),
            TargetRadius = cueRadius,
            DistanceFromCenterOnTouch = distanceFromCenter,
            HoldAverageDrift = holdAverageDrift,
            HoldStabilityH = holdStability,
            ActualPathLength = movementPath,
            IdealPathLength = idealPathLength,
            PathEfficiencyE = pathEfficiency,
            AccuracyA = accuracy,
            ReactionScoreR = reactionScore,
            CoordinationScore = coordinationScore,
            PeakHandSpeed = peakHandSpeed,
            AverageHandSpeed = averageHandSpeed,
            MovementDuration = movementDuration,
            TimeToPeakSpeed = timeToPeakSpeed,
            NumberOfDirectionChanges = directionChangeCount,
            PathSmoothness = pathSmoothness,
            EndpointErrorX = endpointError.x,
            EndpointErrorY = endpointError.y,
            OvershootDistance = overshootDistance,
            UndershootDistance = undershootDistance,
            HandTrackingLostCount = handTrackingLostCount,
            HandTrackingLostDuration = handTrackingLostDuration,
            HeadRotationYAtCueStart = headRotationYAtCueStart,
            HeadRotationYAtTouch = headRotationYAtTouch,
            HeadMovedDuringCue = !float.IsNaN(headDelta) && headDelta >= 10f ? 1 : 0,
            CueVisibleInViewportAtSpawn = cueVisibleInViewportAtSpawn ? 1 : 0,
            MovementOnsetTimeS = kinematics.MovementOnsetTimeS,
            MovementOnsetLatencyS = kinematics.MovementOnsetLatencyS,
            MovementOnsetSpeedThresholdMps = kinematics.MovementOnsetSpeedThresholdMps,
            InitiationLatencyS = kinematics.InitiationLatencyS,
            TransportTimeS = kinematics.TransportTimeS,
            StabilizationTimeS = kinematics.StabilizationTimeS,
            MovementStartPosition = kinematics.MovementStartPatient,
            MovementStartWorldPosition = kinematics.MovementStartWorld,
            ReachAmplitudeM = kinematics.ReachAmplitudeM,
            MeanSpeedMps = averageHandSpeed,
            PeakSpeedMps = peakHandSpeed,
            MeanSpeedNormalized = kinematics.MeanSpeedNormalized,
            PeakSpeedNormalized = kinematics.PeakSpeedNormalized,
            NormalizedJerk = kinematics.NormalizedJerk,
            CorrectiveSubmovementCount = kinematics.CorrectiveSubmovementCount,
            PrimaryVelocityPeakMps = kinematics.PrimaryVelocityPeakMps,
            SecondaryVelocityPeakCount = kinematics.SecondaryVelocityPeakCount,
            InitialDirectionErrorDeg = kinematics.InitialDirectionErrorDeg,
            TrajectoryCorridorRadiusM = kinematics.TrajectoryCorridorRadiusM,
            TimeOutsideTrajectoryCorridorS = kinematics.TimeOutsideTrajectoryCorridorS,
            PercentTransportOutsideCorridor = kinematics.PercentTransportOutsideCorridor,
            MaxTrajectoryDeviationM = kinematics.MaxTrajectoryDeviationM,
            MeanTrajectoryDeviationM = kinematics.MeanTrajectoryDeviationM,
            HeadStartPosition = kinematics.HeadStartPatient,
            HeadContactPosition = kinematics.HeadContactPatient,
            HeadTranslation = kinematics.HeadTranslation,
            HeadTranslationTotalM = kinematics.HeadTranslationTotalM,
            HeadStartRotation = kinematics.HeadStartRotation,
            HeadContactRotation = kinematics.HeadContactRotation,
            HeadDeltaRotation = kinematics.HeadDeltaRotation,
            ContralateralMovementDetected = kinematics.ContralateralMovementDetected ? 1 : 0,
            ContralateralMovementOnsetTimeS = kinematics.ContralateralMovementOnsetTimeS,
            ContralateralMovementOnsetLatencyS = kinematics.ContralateralMovementOnsetLatencyS,
            ContralateralMaxDisplacementM = kinematics.ContralateralMaxDisplacementM,
            ContralateralPathLengthM = kinematics.ContralateralPathLengthM,
            ContralateralPeakSpeedMps = kinematics.ContralateralPeakSpeedMps,
            ContralateralTargetApproachM = kinematics.ContralateralTargetApproachM,
            CueSequenceNumber = cueSequence,
            ElapsedSessionTimeS = kinematics.ElapsedSessionTimeS,
            NormalizedSessionPosition = kinematics.NormalizedSessionPosition,
            PlannedSessionDurationS = kinematics.PlannedSessionDurationS,
            ActualSessionDurationS = kinematics.ActualSessionDurationS,
            IntercueIntervalS = kinematics.IntercueIntervalS,
            TargetRadiusM = cueRadius,
            TargetDiameterM = cueRadius * 2f,
            TargetPosition = ToPatientCenteredPosition(cueCenterWorld),
            Hemispace = cueHemispaceLabel,
            RequiredHand = requiredHandLabel,
            PrecisionMode = GameplayManager.Instance != null ? GameplayManager.Instance.CurrentPrecision.ToString().ToUpperInvariant() : "",
            HoldRequiredS = RequiresHold ? HoldDuration : 0f,
            CueTimeoutS = LifeTime,
            SmartCueEnabled = 0,
            SmartCueZone = "",
            SmartCueZoneClassification = NeuroTapKinematicConfig.DefaultSmartCueZoneClassification,
            CueSpawnMethod = NeuroTapKinematicConfig.DefaultCueSpawnMethod,
            TrajectoryExpectedSampleCount = kinematics.TrajectoryExpectedSampleCount,
            TrajectoryValidSampleCount = kinematics.TrajectoryValidSampleCount,
            TrajectoryValidSamplePercent = kinematics.TrajectoryValidSamplePercent,
            RequiredHandValidSamplePercent = kinematics.RequiredHandValidSamplePercent,
            ContralateralHandValidSamplePercent = kinematics.ContralateralHandValidSamplePercent,
            HeadValidSamplePercent = kinematics.HeadValidSamplePercent,
            KinematicProcessingVersion = NeuroTapKinematicConfig.KinematicProcessingVersion,
            DataSchemaVersion = NeuroTapKinematicConfig.DataSchemaVersion
        };

        GameplayManager.Instance.LogCueResult(result);

        if (eventType == "final" && GameplayManager.Instance != null)
            GameplayManager.Instance.LastCueDisappearanceGameTime = cueEndGameTime;

        if (eventType == "final")
            GameplayManager.Instance.ClearActiveTarget(this);
    }

    public void ForceLogSnapshot(string reason)
    {
        if (finalResultLogged)
            return;

        ReportCueResult(false, reason, "snapshot");
    }

    private struct KinematicSummary
    {
        public float MovementOnsetTimeS;
        public float MovementOnsetLatencyS;
        public float MovementOnsetSpeedThresholdMps;
        public float InitiationLatencyS;
        public float TransportTimeS;
        public float StabilizationTimeS;
        public Vector3 MovementStartPatient;
        public Vector3 MovementStartWorld;
        public float ReachAmplitudeM;
        public float MeanSpeedNormalized;
        public float PeakSpeedNormalized;
        public float NormalizedJerk;
        public int CorrectiveSubmovementCount;
        public float PrimaryVelocityPeakMps;
        public int SecondaryVelocityPeakCount;
        public float InitialDirectionErrorDeg;
        public float TrajectoryCorridorRadiusM;
        public float TimeOutsideTrajectoryCorridorS;
        public float PercentTransportOutsideCorridor;
        public float MaxTrajectoryDeviationM;
        public float MeanTrajectoryDeviationM;
        public Vector3 HeadStartPatient;
        public Vector3 HeadContactPatient;
        public Vector3 HeadTranslation;
        public float HeadTranslationTotalM;
        public Vector3 HeadStartRotation;
        public Vector3 HeadContactRotation;
        public Vector3 HeadDeltaRotation;
        public bool ContralateralMovementDetected;
        public float ContralateralMovementOnsetTimeS;
        public float ContralateralMovementOnsetLatencyS;
        public float ContralateralMaxDisplacementM;
        public float ContralateralPathLengthM;
        public float ContralateralPeakSpeedMps;
        public float ContralateralTargetApproachM;
        public float ElapsedSessionTimeS;
        public float NormalizedSessionPosition;
        public float PlannedSessionDurationS;
        public float ActualSessionDurationS;
        public float IntercueIntervalS;
        public int TrajectoryExpectedSampleCount;
        public int TrajectoryValidSampleCount;
        public float TrajectoryValidSamplePercent;
        public float RequiredHandValidSamplePercent;
        public float ContralateralHandValidSamplePercent;
        public float HeadValidSamplePercent;
    }

    private KinematicSummary BuildKinematicSummary(float cueEndGameTime, float movementPath, bool tapped)
    {
        KinematicSummary summary = EmptyKinematicSummary();
        float cueDuration = Mathf.Max(0f, cueEndGameTime - cueStartGameTime);
        summary.PlannedSessionDurationS = GameplayManager.Instance != null ? GameplayManager.Instance.SessionDuration * 5f : float.NaN;
        summary.ActualSessionDurationS = GameplayManager.Instance != null ? Mathf.Max(0f, cueEndGameTime - GameplayManager.Instance.SessionStartGameTime) : float.NaN;
        summary.ElapsedSessionTimeS = GameplayManager.Instance != null ? Mathf.Max(0f, cueStartGameTime - GameplayManager.Instance.SessionStartGameTime) : float.NaN;
        summary.NormalizedSessionPosition = !float.IsNaN(summary.PlannedSessionDurationS) && summary.PlannedSessionDurationS > 0f
            ? summary.ElapsedSessionTimeS / summary.PlannedSessionDurationS
            : float.NaN;
        summary.IntercueIntervalS = GameplayManager.Instance != null && !float.IsNaN(GameplayManager.Instance.LastCueDisappearanceGameTime)
            ? cueStartGameTime - GameplayManager.Instance.LastCueDisappearanceGameTime
            : float.NaN;

        PopulateTrackingQuality(ref summary, cueDuration);

        if (kinematicSamples.Count < 2)
            return summary;

        summary.MovementOnsetSpeedThresholdMps = CalculateOnsetThreshold(required: true);
        int onsetIndex = FindSustainedOnsetIndex(required: true, summary.MovementOnsetSpeedThresholdMps);
        if (onsetIndex >= 0)
        {
            KinematicSample onset = kinematicSamples[onsetIndex];
            summary.MovementOnsetTimeS = onset.Time;
            summary.MovementOnsetLatencyS = onset.Time - cueStartGameTime;
            summary.InitiationLatencyS = summary.MovementOnsetLatencyS;
            summary.MovementStartWorld = MedianWorldPosition(required: true, onset.Time - NeuroTapKinematicConfig.StartPositionWindowSec, onset.Time);
            summary.MovementStartPatient = ToPatientCenteredPosition(summary.MovementStartWorld);
            summary.HeadStartPatient = MedianHeadPosition(onset.Time - NeuroTapKinematicConfig.StartPositionWindowSec, onset.Time);
            summary.HeadStartRotation = FirstHeadRotationAtOrAfter(onset.Time);
        }

        if (!float.IsNaN(correctArmReactionTime) && !float.IsNaN(summary.MovementOnsetLatencyS))
            summary.TransportTimeS = Mathf.Max(0f, correctArmReactionTime - summary.MovementOnsetLatencyS);

        if (!float.IsNaN(correctArmReactionTime))
        {
            float contactTime = cueStartGameTime + correctArmReactionTime;
            float endRelative = cueEndGameTime - cueStartGameTime;
            summary.StabilizationTimeS = Mathf.Max(0f, endRelative - correctArmReactionTime);
            summary.HeadContactPatient = HeadPositionAtOrBefore(contactTime);
            summary.HeadContactRotation = HeadRotationAtOrBefore(contactTime);
        }

        if (!float.IsNaN(summary.MovementStartPatient.x))
            summary.ReachAmplitudeM = Vector3.Distance(summary.MovementStartPatient, ToPatientCenteredPosition(cueCenterWorld));

        if (!float.IsNaN(summary.ReachAmplitudeM) && summary.ReachAmplitudeM > 0.0001f)
        {
            summary.MeanSpeedNormalized = SafeDivide(speedSampleCount > 0 ? speedSampleSum / speedSampleCount : float.NaN, summary.ReachAmplitudeM);
            summary.PeakSpeedNormalized = SafeDivide(peakHandSpeed, summary.ReachAmplitudeM);
        }

        if (!float.IsNaN(summary.MovementOnsetTimeS) && !float.IsNaN(correctArmReactionTime))
        {
            float contactTime = cueStartGameTime + correctArmReactionTime;
            summary.NormalizedJerk = CalculateNormalizedJerk(summary.MovementOnsetTimeS, contactTime, summary.ReachAmplitudeM);
            CalculateSubmovements(summary.MovementOnsetTimeS, contactTime, ref summary);
            summary.InitialDirectionErrorDeg = CalculateInitialDirectionError(summary.MovementOnsetTimeS, summary.MovementStartPatient);
            CalculateCorridorMetrics(summary.MovementOnsetTimeS, contactTime, summary.MovementStartPatient, ref summary);
        }

        if (!float.IsNaN(summary.HeadStartPatient.x) && !float.IsNaN(summary.HeadContactPatient.x))
        {
            summary.HeadTranslation = summary.HeadContactPatient - summary.HeadStartPatient;
            summary.HeadTranslationTotalM = summary.HeadTranslation.magnitude;
        }

        if (!float.IsNaN(summary.HeadStartRotation.x) && !float.IsNaN(summary.HeadContactRotation.x))
        {
            summary.HeadDeltaRotation = new Vector3(
                Mathf.DeltaAngle(summary.HeadStartRotation.x, summary.HeadContactRotation.x),
                Mathf.DeltaAngle(summary.HeadStartRotation.y, summary.HeadContactRotation.y),
                Mathf.DeltaAngle(summary.HeadStartRotation.z, summary.HeadContactRotation.z));
        }

        PopulateContralateralSummary(ref summary);
        return summary;
    }

    private KinematicSummary EmptyKinematicSummary()
    {
        return new KinematicSummary
        {
            MovementOnsetTimeS = float.NaN,
            MovementOnsetLatencyS = float.NaN,
            MovementOnsetSpeedThresholdMps = float.NaN,
            InitiationLatencyS = float.NaN,
            TransportTimeS = float.NaN,
            StabilizationTimeS = float.NaN,
            MovementStartPatient = MissingVector(),
            MovementStartWorld = MissingVector(),
            ReachAmplitudeM = float.NaN,
            MeanSpeedNormalized = float.NaN,
            PeakSpeedNormalized = float.NaN,
            NormalizedJerk = float.NaN,
            PrimaryVelocityPeakMps = float.NaN,
            InitialDirectionErrorDeg = float.NaN,
            TrajectoryCorridorRadiusM = float.NaN,
            TimeOutsideTrajectoryCorridorS = float.NaN,
            PercentTransportOutsideCorridor = float.NaN,
            MaxTrajectoryDeviationM = float.NaN,
            MeanTrajectoryDeviationM = float.NaN,
            HeadStartPatient = MissingVector(),
            HeadContactPatient = MissingVector(),
            HeadTranslation = MissingVector(),
            HeadTranslationTotalM = float.NaN,
            HeadStartRotation = MissingVector(),
            HeadContactRotation = MissingVector(),
            HeadDeltaRotation = MissingVector(),
            ContralateralMovementOnsetTimeS = float.NaN,
            ContralateralMovementOnsetLatencyS = float.NaN,
            ContralateralMaxDisplacementM = float.NaN,
            ContralateralPathLengthM = float.NaN,
            ContralateralPeakSpeedMps = float.NaN,
            ContralateralTargetApproachM = float.NaN,
            ElapsedSessionTimeS = float.NaN,
            NormalizedSessionPosition = float.NaN,
            PlannedSessionDurationS = float.NaN,
            ActualSessionDurationS = float.NaN,
            IntercueIntervalS = float.NaN,
            TrajectoryValidSamplePercent = float.NaN,
            RequiredHandValidSamplePercent = float.NaN,
            ContralateralHandValidSamplePercent = float.NaN,
            HeadValidSamplePercent = float.NaN
        };
    }

    private void PopulateTrackingQuality(ref KinematicSummary summary, float cueDuration)
    {
        summary.TrajectoryExpectedSampleCount = Mathf.CeilToInt(Mathf.Max(0f, cueDuration) * NeuroTapKinematicConfig.TargetTrajectorySampleRateHz);

        int requiredValid = 0;
        int contralateralValid = 0;
        int headValid = 0;
        int allValid = 0;

        for (int i = 0; i < kinematicSamples.Count; i++)
        {
            KinematicSample sample = kinematicSamples[i];
            if (sample.RequiredValid)
                requiredValid++;
            if (sample.ContralateralValid)
                contralateralValid++;
            if (sample.HeadValid)
                headValid++;
            if (sample.RequiredValid && sample.ContralateralValid && sample.HeadValid)
                allValid++;
        }

        int expected = Mathf.Max(1, summary.TrajectoryExpectedSampleCount);
        summary.TrajectoryValidSampleCount = allValid;
        summary.TrajectoryValidSamplePercent = Percent(allValid, expected);
        summary.RequiredHandValidSamplePercent = Percent(requiredValid, expected);
        summary.ContralateralHandValidSamplePercent = Percent(contralateralValid, expected);
        summary.HeadValidSamplePercent = Percent(headValid, expected);
    }

    private float CalculateOnsetThreshold(bool required)
    {
        List<float> baselineSpeeds = new List<float>();
        float end = cueStartGameTime + NeuroTapKinematicConfig.StartPositionWindowSec;

        for (int i = 0; i < kinematicSamples.Count; i++)
        {
            KinematicSample sample = kinematicSamples[i];
            if (sample.Time > end)
                break;

            if (required && sample.RequiredValid)
                baselineSpeeds.Add(sample.RequiredSpeed);
            else if (!required && sample.ContralateralValid)
                baselineSpeeds.Add(sample.ContralateralSpeed);
        }

        if (baselineSpeeds.Count == 0)
            return NeuroTapKinematicConfig.MovementSpeedThresholdMps;

        float mean = 0f;
        for (int i = 0; i < baselineSpeeds.Count; i++)
            mean += baselineSpeeds[i];
        mean /= baselineSpeeds.Count;

        float variance = 0f;
        for (int i = 0; i < baselineSpeeds.Count; i++)
        {
            float delta = baselineSpeeds[i] - mean;
            variance += delta * delta;
        }
        variance /= baselineSpeeds.Count;

        return Mathf.Max(NeuroTapKinematicConfig.MovementSpeedThresholdMps, mean + 3f * Mathf.Sqrt(variance));
    }

    private int FindSustainedOnsetIndex(bool required, float threshold)
    {
        for (int i = 0; i < kinematicSamples.Count; i++)
        {
            KinematicSample start = kinematicSamples[i];
            bool valid = required ? start.RequiredValid : start.ContralateralValid;
            float speed = required ? start.RequiredSpeed : start.ContralateralSpeed;

            if (!valid || speed < threshold)
                continue;

            float sustainUntil = start.Time + NeuroTapKinematicConfig.MovementSustainDurationSec;
            bool sustained = true;
            bool reachedWindow = false;

            for (int j = i; j < kinematicSamples.Count; j++)
            {
                KinematicSample sample = kinematicSamples[j];
                bool sampleValid = required ? sample.RequiredValid : sample.ContralateralValid;
                float sampleSpeed = required ? sample.RequiredSpeed : sample.ContralateralSpeed;

                if (!sampleValid || sampleSpeed < threshold)
                {
                    sustained = false;
                    break;
                }

                if (sample.Time >= sustainUntil)
                {
                    reachedWindow = true;
                    break;
                }
            }

            if (sustained && reachedWindow)
                return i;
        }

        return -1;
    }

    private Vector3 MedianWorldPosition(bool required, float startTime, float endTime)
    {
        List<float> xs = new List<float>();
        List<float> ys = new List<float>();
        List<float> zs = new List<float>();

        for (int i = 0; i < kinematicSamples.Count; i++)
        {
            KinematicSample sample = kinematicSamples[i];
            if (sample.Time < startTime || sample.Time > endTime)
                continue;

            bool valid = required ? sample.RequiredValid : sample.ContralateralValid;
            if (!valid)
                continue;

            Vector3 position = required ? sample.RequiredWorld : sample.ContralateralWorld;
            xs.Add(position.x);
            ys.Add(position.y);
            zs.Add(position.z);
        }

        if (xs.Count == 0)
            return MissingVector();

        return new Vector3(Median(xs), Median(ys), Median(zs));
    }

    private Vector3 MedianHeadPosition(float startTime, float endTime)
    {
        List<float> xs = new List<float>();
        List<float> ys = new List<float>();
        List<float> zs = new List<float>();

        for (int i = 0; i < kinematicSamples.Count; i++)
        {
            KinematicSample sample = kinematicSamples[i];
            if (!sample.HeadValid || sample.Time < startTime || sample.Time > endTime)
                continue;

            Vector3 patient = ToPatientCenteredPosition(sample.HeadWorld);
            xs.Add(patient.x);
            ys.Add(patient.y);
            zs.Add(patient.z);
        }

        if (xs.Count == 0)
            return MissingVector();

        return new Vector3(Median(xs), Median(ys), Median(zs));
    }

    private Vector3 FirstHeadRotationAtOrAfter(float time)
    {
        for (int i = 0; i < kinematicSamples.Count; i++)
        {
            if (kinematicSamples[i].HeadValid && kinematicSamples[i].Time >= time)
                return kinematicSamples[i].HeadEuler;
        }

        return MissingVector();
    }

    private Vector3 HeadPositionAtOrBefore(float time)
    {
        for (int i = kinematicSamples.Count - 1; i >= 0; i--)
        {
            if (kinematicSamples[i].HeadValid && kinematicSamples[i].Time <= time)
                return ToPatientCenteredPosition(kinematicSamples[i].HeadWorld);
        }

        return MissingVector();
    }

    private Vector3 HeadRotationAtOrBefore(float time)
    {
        for (int i = kinematicSamples.Count - 1; i >= 0; i--)
        {
            if (kinematicSamples[i].HeadValid && kinematicSamples[i].Time <= time)
                return kinematicSamples[i].HeadEuler;
        }

        return MissingVector();
    }

    private void CalculateSubmovements(float onsetTime, float contactTime, ref KinematicSummary summary)
    {
        List<int> peakIndexes = new List<int>();

        for (int i = 1; i < kinematicSamples.Count - 1; i++)
        {
            KinematicSample previous = kinematicSamples[i - 1];
            KinematicSample current = kinematicSamples[i];
            KinematicSample next = kinematicSamples[i + 1];

            if (current.Time < onsetTime || current.Time > contactTime || !current.RequiredValid)
                continue;

            if (current.RequiredSpeed >= previous.RequiredSpeed && current.RequiredSpeed > next.RequiredSpeed)
                peakIndexes.Add(i);
        }

        if (peakIndexes.Count == 0)
            return;

        float primaryPeak = 0f;
        int primaryIndex = peakIndexes[0];
        for (int i = 0; i < peakIndexes.Count; i++)
        {
            float speed = kinematicSamples[peakIndexes[i]].RequiredSpeed;
            if (speed > primaryPeak)
            {
                primaryPeak = speed;
                primaryIndex = peakIndexes[i];
            }
        }

        summary.PrimaryVelocityPeakMps = primaryPeak;
        int secondaryCount = 0;
        float lastCountedTime = kinematicSamples[primaryIndex].Time;

        for (int i = 0; i < peakIndexes.Count; i++)
        {
            int peakIndex = peakIndexes[i];
            if (peakIndex == primaryIndex)
                continue;

            float peakSpeed = kinematicSamples[peakIndex].RequiredSpeed;
            if (peakSpeed < primaryPeak * NeuroTapKinematicConfig.SubmovementMinPeakRatio)
                continue;

            if (Mathf.Abs(kinematicSamples[peakIndex].Time - lastCountedTime) < NeuroTapKinematicConfig.SubmovementMinSeparationSec)
                continue;

            if (!HasMeaningfulValleyBetween(primaryIndex, peakIndex, primaryPeak))
                continue;

            secondaryCount++;
            lastCountedTime = kinematicSamples[peakIndex].Time;
        }

        summary.SecondaryVelocityPeakCount = secondaryCount;
        summary.CorrectiveSubmovementCount = secondaryCount;
    }

    private bool HasMeaningfulValleyBetween(int peakA, int peakB, float primaryPeak)
    {
        int start = Mathf.Min(peakA, peakB);
        int end = Mathf.Max(peakA, peakB);
        if (end - start < 2)
            return false;

        float valley = float.PositiveInfinity;
        for (int i = start + 1; i < end; i++)
            valley = Mathf.Min(valley, kinematicSamples[i].RequiredSpeed);

        float lowerPeak = Mathf.Min(kinematicSamples[peakA].RequiredSpeed, kinematicSamples[peakB].RequiredSpeed);
        return lowerPeak - valley >= primaryPeak * NeuroTapKinematicConfig.SubmovementMinValleyDropRatio;
    }

    private float CalculateInitialDirectionError(float onsetTime, Vector3 movementStartPatient)
    {
        if (float.IsNaN(movementStartPatient.x))
            return float.NaN;

        float windowEnd = onsetTime + NeuroTapKinematicConfig.InitialDirectionWindowSec;
        Vector3 windowPosition = MissingVector();

        for (int i = 0; i < kinematicSamples.Count; i++)
        {
            if (kinematicSamples[i].RequiredValid && kinematicSamples[i].Time >= windowEnd)
            {
                windowPosition = ToPatientCenteredPosition(kinematicSamples[i].RequiredWorld);
                break;
            }
        }

        if (float.IsNaN(windowPosition.x))
            return float.NaN;

        Vector3 targetVector = ToPatientCenteredPosition(cueCenterWorld) - movementStartPatient;
        Vector3 initialVector = windowPosition - movementStartPatient;
        if (targetVector.magnitude < 0.0001f || initialVector.magnitude < 0.0001f)
            return float.NaN;

        float cosTheta = Mathf.Clamp(Vector3.Dot(initialVector, targetVector) / (initialVector.magnitude * targetVector.magnitude), -1f, 1f);
        return Mathf.Acos(cosTheta) * Mathf.Rad2Deg;
    }

    private void CalculateCorridorMetrics(float onsetTime, float contactTime, Vector3 movementStartPatient, ref KinematicSummary summary)
    {
        if (float.IsNaN(movementStartPatient.x))
            return;

        Vector3 target = ToPatientCenteredPosition(cueCenterWorld);
        summary.TrajectoryCorridorRadiusM = Mathf.Max(
            NeuroTapKinematicConfig.TrajectoryCorridorMinRadiusM,
            NeuroTapKinematicConfig.TrajectoryCorridorTargetRadiusMultiplier * cueRadius);

        float outsideTime = 0f;
        float weightedDistance = 0f;
        float totalTime = 0f;
        float maxDeviation = 0f;

        for (int i = 1; i < kinematicSamples.Count; i++)
        {
            KinematicSample previous = kinematicSamples[i - 1];
            KinematicSample current = kinematicSamples[i];
            if (!current.RequiredValid || current.Time < onsetTime || current.Time > contactTime)
                continue;

            float dt = Mathf.Max(0f, current.Time - previous.Time);
            Vector3 point = ToPatientCenteredPosition(current.RequiredWorld);
            float deviation = DistanceToSegment(point, movementStartPatient, target);
            maxDeviation = Mathf.Max(maxDeviation, deviation);
            weightedDistance += deviation * dt;
            totalTime += dt;

            if (deviation > summary.TrajectoryCorridorRadiusM)
                outsideTime += dt;
        }

        summary.TimeOutsideTrajectoryCorridorS = outsideTime;
        summary.PercentTransportOutsideCorridor = totalTime > 0f ? 100f * outsideTime / totalTime : float.NaN;
        summary.MaxTrajectoryDeviationM = totalTime > 0f ? maxDeviation : float.NaN;
        summary.MeanTrajectoryDeviationM = totalTime > 0f ? weightedDistance / totalTime : float.NaN;
    }

    private float CalculateNormalizedJerk(float onsetTime, float contactTime, float reachAmplitude)
    {
        if (float.IsNaN(reachAmplitude) || reachAmplitude < 0.0001f)
            return float.NaN;

        List<KinematicSample> samples = GetRequiredWindowSamples(onsetTime, contactTime);
        if (samples.Count < 5)
            return float.NaN;

        List<Vector3> smoothed = new List<Vector3>(samples.Count);
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int j = Mathf.Max(0, i - 1); j <= Mathf.Min(samples.Count - 1, i + 1); j++)
            {
                sum += samples[j].RequiredWorld;
                count++;
            }

            smoothed.Add(sum / Mathf.Max(1, count));
        }

        float integral = 0f;
        for (int i = 3; i < smoothed.Count; i++)
        {
            float dt = Mathf.Max(0.0001f, samples[i].Time - samples[i - 1].Time);
            Vector3 jerk = (smoothed[i] - 3f * smoothed[i - 1] + 3f * smoothed[i - 2] - smoothed[i - 3]) / (dt * dt * dt);
            integral += jerk.sqrMagnitude * dt;
        }

        float transportTime = Mathf.Max(0f, contactTime - onsetTime);
        if (transportTime < 0.0001f)
            return float.NaN;

        return (Mathf.Pow(transportTime, 5f) / (reachAmplitude * reachAmplitude)) * integral;
    }

    private List<KinematicSample> GetRequiredWindowSamples(float startTime, float endTime)
    {
        List<KinematicSample> result = new List<KinematicSample>();
        for (int i = 0; i < kinematicSamples.Count; i++)
        {
            KinematicSample sample = kinematicSamples[i];
            if (sample.RequiredValid && sample.Time >= startTime && sample.Time <= endTime)
                result.Add(sample);
        }

        return result;
    }

    private void PopulateContralateralSummary(ref KinematicSummary summary)
    {
        Vector3 start = MedianWorldPosition(required: false, cueStartGameTime, cueStartGameTime + NeuroTapKinematicConfig.StartPositionWindowSec);
        if (float.IsNaN(start.x))
            return;

        float onsetThreshold = CalculateOnsetThreshold(required: false);
        int onsetIndex = FindSustainedOnsetIndex(required: false, onsetThreshold);
        if (onsetIndex >= 0)
        {
            summary.ContralateralMovementDetected = true;
            summary.ContralateralMovementOnsetTimeS = kinematicSamples[onsetIndex].Time;
            summary.ContralateralMovementOnsetLatencyS = kinematicSamples[onsetIndex].Time - cueStartGameTime;
        }

        float maxDisplacement = 0f;
        float pathLength = 0f;
        float peakSpeed = 0f;
        float initialTargetDistance = Vector3.Distance(ToPatientCenteredPosition(start), ToPatientCenteredPosition(cueCenterWorld));
        float minimumTargetDistance = initialTargetDistance;
        bool hasPrevious = false;
        Vector3 previous = Vector3.zero;

        for (int i = 0; i < kinematicSamples.Count; i++)
        {
            KinematicSample sample = kinematicSamples[i];
            if (!sample.ContralateralValid)
                continue;

            Vector3 patient = ToPatientCenteredPosition(sample.ContralateralWorld);
            Vector3 startPatient = ToPatientCenteredPosition(start);
            maxDisplacement = Mathf.Max(maxDisplacement, Vector3.Distance(startPatient, patient));
            minimumTargetDistance = Mathf.Min(minimumTargetDistance, Vector3.Distance(patient, ToPatientCenteredPosition(cueCenterWorld)));
            peakSpeed = Mathf.Max(peakSpeed, sample.ContralateralSpeed);

            if (hasPrevious)
                pathLength += Vector3.Distance(previous, patient);

            previous = patient;
            hasPrevious = true;
        }

        summary.ContralateralMaxDisplacementM = maxDisplacement;
        summary.ContralateralPathLengthM = pathLength;
        summary.ContralateralPeakSpeedMps = peakSpeed;
        summary.ContralateralTargetApproachM = initialTargetDistance - minimumTargetDistance;
    }

    private Vector3 ToPatientCenteredPosition(Vector3 worldPosition)
    {
        if (float.IsNaN(worldPosition.x))
            return MissingVector();

        Camera camera = Camera.main;
        if (camera == null)
            return worldPosition;

        return camera.transform.InverseTransformPoint(worldPosition);
    }

    private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float denominator = Vector3.Dot(ab, ab);
        if (denominator < 0.0001f)
            return Vector3.Distance(point, a);

        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / denominator);
        return Vector3.Distance(point, a + t * ab);
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        if (values.Count % 2 == 1)
            return values[middle];

        return (values[middle - 1] + values[middle]) * 0.5f;
    }

    private static float Percent(int numerator, int denominator)
    {
        return denominator > 0 ? 100f * numerator / denominator : float.NaN;
    }

    public void ForceLogFinal(string reason)
    {
        if (finalResultLogged)
            return;

        completed = true;
        ReportCueResult(false, reason, "final");
    }

    private Transform GetRequiredAnchor()
    {
        if (GameplayManager.Instance == null)
            return null;

        return requiredArmSide == 0 ? GameplayManager.Instance.LeftHandAnchor : GameplayManager.Instance.RightHandAnchor;
    }

    private Transform GetWrongAnchor()
    {
        if (GameplayManager.Instance == null)
            return null;

        return requiredArmSide == 0 ? GameplayManager.Instance.RightHandAnchor : GameplayManager.Instance.LeftHandAnchor;
    }

    private Vector3 GetCueCenter()
    {
        Collider ownCollider = GetComponent<Collider>();

        if (ownCollider != null)
            return ownCollider.bounds.center;

        Renderer renderer = GetComponentInChildren<Renderer>();

        if (renderer != null)
            return renderer.bounds.center;

        return transform.position;
    }

    private float GetCueRadius()
    {
        Collider ownCollider = GetComponent<Collider>();

        if (ownCollider != null)
            return Mathf.Max(ownCollider.bounds.extents.x, ownCollider.bounds.extents.y, ownCollider.bounds.extents.z);

        Renderer renderer = GetComponentInChildren<Renderer>();

        if (renderer != null)
            return Mathf.Max(renderer.bounds.extents.x, renderer.bounds.extents.y, renderer.bounds.extents.z);

        return 0.1f;
    }

    private float GetRuntimeTouchRadius()
    {
        Collider cueCollider = GetComponent<Collider>();

        if (cueCollider != null)
        {
            Bounds bounds = cueCollider.bounds;
            return Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        }

        return TouchDetectionRadius;
    }

    private float DistanceFromCueCenter(Vector3 position)
    {
        if (float.IsNaN(position.x))
            return float.NaN;

        return Vector3.Distance(cueCenterWorld, position);
    }

    private Vector2 GetEndpointError()
    {
        if (float.IsNaN(completionTouchWorld.x))
            return new Vector2(float.NaN, float.NaN);

        Vector3 errorWorld = completionTouchWorld - cueCenterWorld;

        if (Camera.main == null)
            return new Vector2(errorWorld.x, errorWorld.y);

        Vector3 errorView = Camera.main.transform.InverseTransformVector(errorWorld);
        return new Vector2(errorView.x, errorView.y);
    }

    private static float SafeDivide(float numerator, float denominator)
    {
        if (float.IsNaN(numerator) || float.IsNaN(denominator) || Mathf.Abs(denominator) < 0.0001f)
            return float.NaN;

        return numerator / denominator;
    }

    private static float ClampMetric(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return float.NaN;

        return Mathf.Clamp01(value);
    }

    private static Vector3 MissingVector()
    {
        return new Vector3(float.NaN, float.NaN, float.NaN);
    }
}
