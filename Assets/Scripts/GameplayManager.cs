using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GameplayManager : MonoBehaviour
{
    public static GameplayManager Instance;

    [Header("UI")]
    public TMP_Text LeftScoreText;
    public TMP_Text RightScoreText;
    public TMP_Text InstructionText;
    public TMP_Text TimerText;
    public TMP_Text TimerText2;
    public TMP_Text PrecisionText;
    public TMP_Text LevelText;

    [Header("Target")]
    public Transform TargetSpawnPoint;
    public GameObject RedTargetPrefab;
    public GameObject BlueTargetPrefab;

    private GameObject currentTarget;
    private TargetInteraction activeTargetInteraction;
    private Vector3 targetBaseScale = Vector3.one;

    [Header("Timer")]
    public Image TimerFillImage;

    [Header("Feedback")]
    public TMP_Text FeedbackText;

    [Header("Gameplay UI")]
    [Tooltip("Head-locked HUD (scores, timer, instructions) under CenterEyeAnchor — object named 'Gameplay'.")]
    public GameObject GameplayScreen;

    [Header("Start Menu")]
    [Tooltip("World-fixed Main Menu Canvas (Start Screen, Exit, Results).")]
    public GameObject MainMenuCanvas;
    public GameObject StartScreen;
    [Tooltip("World panel under Main Menu Canvas that holds Exit + Results (Gameplay Screen).")]
    public GameObject WorldGameplayPanel;
    public GameObject ExitButton;
    public bool CreateRuntimeStartMenu = true;
    public float PostGameReturnToStartDelay = 3f;

    [Header("Pointer Depth")]
    [Tooltip("Start menu and results. Meters in front of the headset. Change this during Play to test pointer clicks.")]
    [Range(0.4f, 2f)]
    [SerializeField] private float startMenuDistanceMeters = 1.1f;
    [Tooltip("World-space height of the start/results menu. 0 = eye height when the menu was placed.")]
    [SerializeField] private float startMenuHeightMeters = 0f;
    [Tooltip("Gameplay Exit button. Meters in front of the headset. Change this during Play to test pointer clicks.")]
    [Range(0.4f, 2f)]
    [SerializeField] private float gameplayExitDistanceMeters = 1.1f;
    [Tooltip("World-space height of the gameplay Exit button.")]
    [SerializeField] private float gameplayExitHeightMeters = 1.35f;
    public float StartMenuDistanceMeters => startMenuDistanceMeters;
    private Vector3 startMenuPlaceOrigin;
    private Vector3 startMenuPlaceForward = Vector3.forward;
    private float startMenuPlaceEyeHeight = 1.5f;
    private bool startMenuPlaceCaptured;
    private Vector3 gameplayExitPlaceOrigin;
    private Vector3 gameplayExitPlaceForward = Vector3.forward;

    [Header("Result Screen")]
    public GameObject ResultScreen;

    [Header("Results")]
    public TMP_Text FinalLeftScoreText;
    public TMP_Text FinalRightScoreText;
    public TMP_Text FinalTotalScoreText;
    public TMP_Text FinalAccuracyText;

    [Header("Session")]
    [Tooltip("Seconds per level. Set any value (e.g. 6 for quick tests, 60 for normal play).")]
    [SerializeField] private float sessionDuration = 60f;
    public float SessionDuration
    {
        get => sessionDuration;
        set => sessionDuration = Mathf.Max(1f, value);
    }
    // Runtime-only — not serialized so scene/Inspector defaults cannot leave the countdown stuck.
    [System.NonSerialized] public float TimeRemaining;
    [System.NonSerialized] public bool GameRunning;

    [System.NonSerialized] private bool sessionActive;
    [System.NonSerialized] private float sessionEndsAtUnscaled;
    [System.NonSerialized] private bool gameplaySessionStarted;
    [System.NonSerialized] private NeuroCueSessionClock sessionClock;
    [System.NonSerialized] public float SessionStartGameTime;
    [System.NonSerialized] public float LastCueDisappearanceGameTime = float.NaN;

    [Header("Levels")]
    public int CurrentLevel = 1;
    private const int MaxLevels = 5;

    [Header("Precision")]
    public PrecisionMode CurrentPrecision = PrecisionMode.Low;

    public enum PrecisionMode { Low, Medium, High }
    public enum ContactRegion { None, Limb, Hand, ThumbOrIndex }

    [Header("Cue Logic")]
    public int cueCount;
    private int cuesSinceLastHold;
    public bool CurrentCueRequiresHold;
    public bool RequireLeftHand = true;
    [Tooltip("Default assessment behavior: Levels 1-2 alternate; Level 3 mostly alternates; Levels 4-5 use weighted random with repeat limits.")]
    public bool UseLevelBasedAssessmentHandProgression = true;
    public NeuroCueSideScheduleSettings RequiredHandSchedule = new NeuroCueSideScheduleSettings();
    public NeuroCueLocationScheduleSettings CueLocationSchedule = new NeuroCueLocationScheduleSettings();

    [Header("Assessment Grid v2")]
    public bool UseAssessmentGridV2 = true;
    private const string AssessmentGridV2ProtocolVersion = "assessment_grid_v2";
    private const float AssessmentGridV2DepthM = 0.55f;
    private const float AssessmentGridEpsilon = 0.001f;
    private static readonly float[] AssessmentGridXPositionsM =
    {
        -0.30f, -0.25f, -0.20f, -0.15f, -0.10f, -0.05f,
         0.05f,  0.10f,  0.15f,  0.20f,  0.25f,  0.30f
    };
    private static readonly float[] AssessmentGridYPositionsM =
    {
        -0.30f, -0.20f, -0.10f, 0.00f, 0.10f
    };

    [Header("Cue Timing")]
    public float CueLifeTime = 8f;
    public float CueHoldDuration = 1f;
    public float PromptVisibleDuration = 10f;

    [Header("Hold Settings")]
    [Tooltip("When enabled, hold cues require 2 seconds instead of the default 1 second.")]
    public bool UseTwoSecondHold;

    [Header("Cue Placement")]
    public float CueHorizontalRange = 0.22f;
    public float CueVerticalMin = -0.45f;
    public float CueVerticalMax = 0.15f;
    public float CueDepthRange = 0.04f;
    public float MinDistanceFromPreviousCue = 0.14f;
    public float MinDistanceFromHands = 0.16f;
    public bool ClampCueBelowEyeLevel = true;
    public float CueMaxBelowEyes = 0.15f;
    public float CueMinBelowEyes = 0.85f;

    [Header("Client Cue Placement")]
    [Tooltip("Fixed forward distance from the headset. Cues always spawn at this reach distance.")]
    public float CueDistanceFromHeadset = 0.55f;
    [Tooltip("Hard cap on horizontal distance from the headset (meters). Prevents side offsets from pushing cues out of reach.")]
    public float MaxCueReachDistance = 0.70f;
    public float CueMinHeightRelativeToEyes = -0.35f;
    public float CueMaxHeightRelativeToEyes = 0.10f;
    public float FirstCueHeightRelativeToEyes = -0.05f;
    public float CueSideSpread = 0.18f;
    public float CueDepthJitter = 0.0f;
    public float BetweenCuePause = 1f;
    public bool EveryCueRequiresHold = true;
    [Tooltip("If true, keeps left/right play direction stable. Distance is still always from the current headset.")]
    public bool LockPlayAreaAtSessionStart = true;
    [Tooltip("Minimum camera Y (meters) before the first cue spawns. Prevents cues on the floor while tracking initializes.")]
    [SerializeField] private float minCameraHeightForFirstCue = 1.0f;
    [Tooltip("Max seconds to wait for head tracking before spawning the first cue anyway.")]
    [SerializeField] private float firstCueTrackingTimeout = 3f;

    private Coroutine pendingSpawnCoroutine;
    private bool levelAdvancePending;

    [Header("Unity / XR Simulator Testing")]
    public bool UnityTestingMode;
    public bool EnableEditorKeyboardStart = true;
    public Vector3 UnityTestingCueOffset = new Vector3(0f, 0.45f, 0f);
    public float UnityTestingTouchRadiusMultiplier = 2f;

    public Vector3 ActiveCueTestingOffset => UnityTestingMode ? UnityTestingCueOffset : Vector3.zero;
    public float ActiveTouchRadiusMultiplier => UnityTestingMode ? Mathf.Max(1f, UnityTestingTouchRadiusMultiplier) : 1f;

    [Header("Cue Facing")]
    public bool KeepCueFacingCameraAfterSpawn = false;

    [Header("Runtime HUD Layout")]
    public bool ApplyRuntimeHudSpacing = true;
    public RectTransform UpperHudRoot;
    public RectTransform LowerInstructionRoot;
    [Tooltip("Push top score/timer HUD slightly up.")]
    public float UpperHudYOffset = 55f;
    [Tooltip("Extra downward shift for the instruction bar after scene placement (Stefan slide 3).")]
    public float LowerHudYOffset = -40f;

    [Header("Wrong Hit")]
    [SerializeField] private float wrongHitDelay = 0.5f;
    private float wrongHitCooldown;
    private Coroutine wrongFeedbackCoroutine;

    [Header("Hand Anchors")]
    public Transform LeftHandAnchor;
    public Transform RightHandAnchor;

    [Header("Score Glow")]
    public GameObject LeftScoreGlow;
    public GameObject RightScoreGlow;

    [Header("Data Logging")]
    public bool EnableDataLogging = true;
    public string DataFolderName = "NeuroCueData";
    public string AssessmentOrTrainingMode = "assessment";
    public string AssessmentProtocolVersion = "assessment_grid_v2";
    public string AppVersionOverride = "";
    public bool IsPracticeTrial;
    public int AssessmentRepetitionNumber;
    public int AssessmentGridRow;
    public int AssessmentGridColumn;

    [Header("Research Placeholders")]
    public string ParticipantId = "";
    public int SessionNumber = 1;
    public string DaysSinceStrokeOrInjury = "";
    public string AffectedBodySide = "";
    public string LesionSide = "";
    public string SuspectedNeglectSide = "";
    public string DominantHand = "";
    public string HeadsetModel = "Meta Quest";

    private int leftScore;
    private int rightScore;
    private int correctHits;
    private int wrongHits;
    private string sessionId;
    private string dataFilePath;
    private float promptShownAt;
    private bool hudSpacingApplied;

    private const string CsvHeader =
        "participant_id,session_number,days_since_stroke_or_injury,affected_body_side,lesion_side,suspected_neglect_side,dominant_hand," +
        "assessment_or_training_mode,assessment_protocol_version,app_version,headset_model,is_practice_trial,assessment_repetition_number," +
        "assessment_grid_row,assessment_grid_column,randomization_order,session_id,event_type,cue_sequence,cue_number,level,cue_condition_id," +
        "arm_side,required_hand_label,cue_hemispace,cue_hemispace_label,cross_midline,horizontal_zone,vertical_zone,depth_zone," +
        "intended_offset_x_m,intended_offset_y_m,intended_depth_m,level_crossing_probability,level_side_offset_m,tapped,requires_hold,completion_reason," +
        "cue_start_utc,cue_end_utc,cue_start_game_time,cue_end_game_time,any_arm_reaction_time,correct_arm_reaction_time," +
        "movement_start_time,engagement_time,seconds_held,wrong_attempts,cue_world_x,cue_world_y,cue_world_z,cue_viewport_x,cue_viewport_y," +
        "cue_screen_x,cue_screen_y,optimal_index_world_x,optimal_index_world_y,optimal_index_world_z,first_any_arm_side," +
        "first_any_touch_x,first_any_touch_y,first_any_touch_z,first_correct_touch_x,first_correct_touch_y,first_correct_touch_z," +
        "completion_touch_x,completion_touch_y,completion_touch_z,contact_region,target_radius,distance_from_center_on_touch," +
        "hold_average_drift,hold_stability_h,actual_path_length,ideal_path_length,path_efficiency_e,accuracy_a,reaction_score_r,coordination_score," +
        "peak_hand_speed,average_hand_speed,movement_duration,time_to_peak_speed,number_of_direction_changes,path_smoothness," +
        "endpoint_error_x,endpoint_error_y,overshoot_distance,undershoot_distance,hand_tracking_lost_count,hand_tracking_lost_duration," +
        "head_rotation_y_at_cue_start,head_rotation_y_at_touch,head_moved_during_cue,cue_visible_in_viewport_at_spawn," +
        "movement_onset_time_s,movement_onset_latency_s,movement_onset_speed_threshold_mps,initiation_latency_s,transport_time_s,stabilization_time_s," +
        "movement_start_x_m,movement_start_y_m,movement_start_z_m,movement_start_world_x_m,movement_start_world_y_m,movement_start_world_z_m," +
        "reach_amplitude_m,mean_speed_mps,peak_speed_mps,mean_speed_normalized,peak_speed_normalized,normalized_jerk," +
        "corrective_submovement_count,primary_velocity_peak_mps,secondary_velocity_peak_count,initial_direction_error_deg," +
        "trajectory_corridor_radius_m,time_outside_trajectory_corridor_s,percent_transport_outside_corridor,max_trajectory_deviation_m,mean_trajectory_deviation_m," +
        "head_start_x_m,head_start_y_m,head_start_z_m,head_contact_x_m,head_contact_y_m,head_contact_z_m," +
        "head_translation_x_m,head_translation_y_m,head_translation_z_m,head_translation_total_m," +
        "head_start_yaw_deg,head_start_pitch_deg,head_start_roll_deg,head_contact_yaw_deg,head_contact_pitch_deg,head_contact_roll_deg," +
        "head_delta_yaw_deg,head_delta_pitch_deg,head_delta_roll_deg,contralateral_movement_detected,contralateral_movement_onset_time_s," +
        "contralateral_movement_onset_latency_s,contralateral_max_displacement_m,contralateral_path_length_m,contralateral_peak_speed_mps,contralateral_target_approach_m," +
        "cue_sequence_number,elapsed_session_time_s,normalized_session_position,planned_session_duration_s,actual_session_duration_s,intercue_interval_s," +
        "target_radius_m,target_diameter_m,target_x_m,target_y_m,target_z_m,hemispace,required_hand,precision_mode,hold_required_s,cue_timeout_s," +
        "smart_cue_enabled,smart_cue_zone,smart_cue_zone_classification,cue_spawn_method,trajectory_expected_sample_count,trajectory_valid_sample_count," +
        "trajectory_valid_sample_percent,required_hand_valid_sample_percent,contralateral_hand_valid_sample_percent,head_valid_sample_percent," +
        "kinematic_processing_version,data_schema_version";

    public struct CueResult
    {
        public string SessionId;
        public string EventType;
        public int CueSequence;
        public int CueNumber;
        public int Level;
        public string ParticipantId;
        public int SessionNumber;
        public string DaysSinceStrokeOrInjury;
        public string AffectedBodySide;
        public string LesionSide;
        public string SuspectedNeglectSide;
        public string DominantHand;
        public string AssessmentOrTrainingMode;
        public string AssessmentProtocolVersion;
        public string AppVersion;
        public string HeadsetModel;
        public int IsPracticeTrial;
        public int AssessmentRepetitionNumber;
        public int AssessmentGridRow;
        public int AssessmentGridColumn;
        public int RandomizationOrder;
        public string CueConditionId;
        public int ArmSide;
        public string RequiredHandLabel;
        public int CueHemispace;
        public string CueHemispaceLabel;
        public int CrossMidline;
        public string HorizontalZone;
        public string VerticalZone;
        public string DepthZone;
        public float IntendedOffsetXM;
        public float IntendedOffsetYM;
        public float IntendedDepthM;
        public float LevelCrossingProbability;
        public float LevelSideOffsetM;
        public int Tapped;
        public int RequiresHold;
        public string CompletionReason;
        public string CueStartUtc;
        public string CueEndUtc;
        public float CueStartGameTime;
        public float CueEndGameTime;
        public float AnyArmReactionTime;
        public float CorrectArmReactionTime;
        public float MovementStartTime;
        public float EngagementTime;
        public float SecondsHeld;
        public int WrongAttempts;
        public Vector3 CueWorldPosition;
        public Vector2 CueViewportPosition;
        public Vector2 CueScreenPosition;
        public Vector3 OptimalIndexWorldPosition;
        public string FirstAnyArmSide;
        public Vector3 FirstAnyTouchWorld;
        public Vector3 FirstCorrectTouchWorld;
        public Vector3 CompletionTouchWorld;
        public string ContactRegion;
        public float TargetRadius;
        public float DistanceFromCenterOnTouch;
        public float HoldAverageDrift;
        public float HoldStabilityH;
        public float ActualPathLength;
        public float IdealPathLength;
        public float PathEfficiencyE;
        public float AccuracyA;
        public float ReactionScoreR;
        public float CoordinationScore;
        public float PeakHandSpeed;
        public float AverageHandSpeed;
        public float MovementDuration;
        public float TimeToPeakSpeed;
        public int NumberOfDirectionChanges;
        public float PathSmoothness;
        public float EndpointErrorX;
        public float EndpointErrorY;
        public float OvershootDistance;
        public float UndershootDistance;
        public int HandTrackingLostCount;
        public float HandTrackingLostDuration;
        public float HeadRotationYAtCueStart;
        public float HeadRotationYAtTouch;
        public int HeadMovedDuringCue;
        public int CueVisibleInViewportAtSpawn;
        public float MovementOnsetTimeS;
        public float MovementOnsetLatencyS;
        public float MovementOnsetSpeedThresholdMps;
        public float InitiationLatencyS;
        public float TransportTimeS;
        public float StabilizationTimeS;
        public Vector3 MovementStartPosition;
        public Vector3 MovementStartWorldPosition;
        public float ReachAmplitudeM;
        public float MeanSpeedMps;
        public float PeakSpeedMps;
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
        public Vector3 HeadStartPosition;
        public Vector3 HeadContactPosition;
        public Vector3 HeadTranslation;
        public float HeadTranslationTotalM;
        public Vector3 HeadStartRotation;
        public Vector3 HeadContactRotation;
        public Vector3 HeadDeltaRotation;
        public int ContralateralMovementDetected;
        public float ContralateralMovementOnsetTimeS;
        public float ContralateralMovementOnsetLatencyS;
        public float ContralateralMaxDisplacementM;
        public float ContralateralPathLengthM;
        public float ContralateralPeakSpeedMps;
        public float ContralateralTargetApproachM;
        public int CueSequenceNumber;
        public float ElapsedSessionTimeS;
        public float NormalizedSessionPosition;
        public float PlannedSessionDurationS;
        public float ActualSessionDurationS;
        public float IntercueIntervalS;
        public float TargetRadiusM;
        public float TargetDiameterM;
        public Vector3 TargetPosition;
        public string Hemispace;
        public string RequiredHand;
        public string PrecisionMode;
        public float HoldRequiredS;
        public float CueTimeoutS;
        public int SmartCueEnabled;
        public string SmartCueZone;
        public string SmartCueZoneClassification;
        public string CueSpawnMethod;
        public int TrajectoryExpectedSampleCount;
        public int TrajectoryValidSampleCount;
        public float TrajectoryValidSamplePercent;
        public float RequiredHandValidSamplePercent;
        public float ContralateralHandValidSamplePercent;
        public float HeadValidSamplePercent;
        public string KinematicProcessingVersion;
        public string DataSchemaVersion;
    }

    private struct AssessmentGridCell
    {
        public int Row;
        public int Column;
        public float IntendedX;
        public float IntendedY;
        public float IntendedZ;
        public int Hemispace;

        public bool CrossesMidlineForHand(int requiredArmSide)
        {
            return requiredArmSide == 0 ? Hemispace == 1 : Hemispace == 0;
        }
    }

    private bool hasSpawnedFirstCue;
    private bool repeatHoldCueAfterMiss;
    private bool sessionPlayAreaLocked;
    private readonly List<float> pendingHeightOffsets = new List<float>();
    private readonly NeuroCueSideScheduler requiredHandScheduler = new NeuroCueSideScheduler();
    private readonly NeuroCueLocationScheduler cueLocationScheduler = new NeuroCueLocationScheduler();
    private NeuroCueLocationResult currentCueLocation = new NeuroCueLocationResult { Hemispace = NeuroCueSide.Left, CrossesMidline = false };
    private readonly List<AssessmentGridCell> assessmentGridSequence = new List<AssessmentGridCell>();
    private AssessmentGridCell currentAssessmentGridCell;
    private bool hasCurrentAssessmentGridCell;
    private int assessmentGridSequenceIndex;
    private int assessmentGridRepetitionNumber = 1;
    private int currentRandomizationOrder;
    private Vector3 sessionPlayOrigin = Vector3.zero;
    private Vector3 sessionPlayForward = Vector3.forward;
    private Vector3 sessionPlayRight = Vector3.right;
    private Coroutine returnToStartCoroutine;
    private Coroutine placeStartMenuCoroutine;
    private Toggle holdDurationToggle;
    private GameObject gameplayExitCanvas;
    private void Awake()
    {
        Instance = this;
        sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        NeuroCueVrUiInteractor.EnsureExists();
        EnsureSessionClock();

        // Default assessment hand schedule (Stefan V4):
        // Levels 1-2 alternate; Level 3 mostly alternates; Levels 4-5 weighted random with repeat limits.
        if (RequiredHandSchedule == null)
            RequiredHandSchedule = new NeuroCueSideScheduleSettings();

        if (UseLevelBasedAssessmentHandProgression)
            RequiredHandSchedule.Mode = NeuroCueScheduleMode.LevelBasedAssessment;
    }

    private void EnsureSessionClock()
    {
        if (sessionClock == null)
            sessionClock = GetComponent<NeuroCueSessionClock>();
        if (sessionClock == null)
            sessionClock = gameObject.AddComponent<NeuroCueSessionClock>();
    }

    private void Start()
    {
        if (RedTargetPrefab != null)
            targetBaseScale = RedTargetPrefab.transform.localScale;

        InitializeDataLogging();
        EnsureUiReferences();
        ApplyHudSpacingOnce();
        TimeRemaining = sessionDuration;

        ApplyHoldDurationSetting();
        // Apply saved patient profile defaults (username, session minutes → per-level duration).
        NeuroTapPatientProfile.ApplyToGameplay(this, guestSession: false);
        EnsureStartMenu();
        WireStartMenuButtons();
        ShowStartMenu();
        UpdateUI();
    }

    public void StartGame()
    {
        // VR poke/ray (and duplicate UI modules) can re-fire Play every frame.
        // Re-entering here resets TimeRemaining to 60 and freezes the HUD at 01:00.
        if (gameplaySessionStarted)
            return;

        if (returnToStartCoroutine != null)
        {
            StopCoroutine(returnToStartCoroutine);
            returnToStartCoroutine = null;
        }

        if (placeStartMenuCoroutine != null)
        {
            StopCoroutine(placeStartMenuCoroutine);
            placeStartMenuCoroutine = null;
        }

        ApplyHoldDurationSetting();
        ResetSessionScores();

        // Clamp so designer can use any positive duration (6s test, 60s play, etc.).
        sessionDuration = Mathf.Max(1f, sessionDuration);

        levelAdvancePending = false;
        SessionStartGameTime = Time.time;
        LastCueDisappearanceGameTime = float.NaN;
        sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        dataFilePath = null;
        InitializeDataLogging();

        gameplaySessionStarted = true;
        ShowGameplayUi();
        ResolveTimerUiReferences();
        BeginSessionTimer(sessionDuration);

        StartCoroutine(SpawnFirstTargetWhenCameraReady());
        UpdateUI();
    }

    public void ReturnToStartMenu()
    {
        gameplaySessionStarted = false;
        StopSessionTimer();
        sessionPlayAreaLocked = false;

        if (pendingSpawnCoroutine != null)
        {
            StopCoroutine(pendingSpawnCoroutine);
            pendingSpawnCoroutine = null;
        }

        if (currentTarget != null)
        {
            Destroy(currentTarget);
            currentTarget = null;
        }

        activeTargetInteraction = null;
        ShowStartMenu();
    }

    public void SetUseTwoSecondHold(bool enabled)
    {
        UseTwoSecondHold = enabled;
        ApplyHoldDurationSetting();
    }

    private void ShowStartMenu()
    {
        gameplaySessionStarted = false;
        StopSessionTimer();
        EnsureUiReferences();
        EnsureQuitButtons();

        if (returnToStartCoroutine != null)
        {
            StopCoroutine(returnToStartCoroutine);
            returnToStartCoroutine = null;
        }

        if (MainMenuCanvas != null)
            MainMenuCanvas.SetActive(true);

        if (StartScreen != null)
        {
            SetStartScreenInteractable(true);
            StartScreen.SetActive(true);
        }

        if (WorldGameplayPanel != null)
            WorldGameplayPanel.SetActive(false);

        if (ResultScreen != null)
            ResultScreen.SetActive(false);

        if (ExitButton != null)
            ExitButton.SetActive(false);

        SetGameplayExitCanvasVisible(false);
        SetStartMenuExitVisible(true);
        SetGameplayHudVisible(false);

        if (placeStartMenuCoroutine != null)
            StopCoroutine(placeStartMenuCoroutine);

        placeStartMenuCoroutine = StartCoroutine(PlaceMainMenuWhenReady());
    }

    private void ShowGameplayUi()
    {
        EnsureUiReferences();
        EnsureQuitButtons();

        if (WorldGameplayPanel != null)
            WorldGameplayPanel.SetActive(false);

        if (ResultScreen != null)
            ResultScreen.SetActive(false);

        // Copy pose/ISDK from the live start menu, then hide it. Hiding first
        // left Gameplay Exit on a different canvas that never received clicks.
        ShowGameplayExitInFrontOfUser();

        if (MainMenuCanvas != null)
            MainMenuCanvas.SetActive(false);

        if (StartScreen != null)
        {
            StartScreen.SetActive(false);
            SetStartScreenInteractable(false);
        }

        SetStartMenuExitVisible(false);
        SetGameplayHudRaycastsEnabled(false);
        SetGameplayHudVisible(true);

        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);

        if (NeuroCueVrUiInteractor.Instance != null)
            NeuroCueVrUiInteractor.Instance.RefreshRaycasters();
    }

    private IEnumerator PlaceMainMenuWhenReady()
    {
        const float timeout = 4f;
        float waited = 0f;

        while (waited < timeout)
        {
            if (TryPlaceMainMenuInWorld())
            {
                placeStartMenuCoroutine = null;
                yield break;
            }

            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        // Last attempt even if tracking height is still low.
        TryPlaceMainMenuInWorld(force: true);
        placeStartMenuCoroutine = null;
    }

    private bool TryPlaceMainMenuInWorld(bool force = false)
    {
        if (MainMenuCanvas == null || Camera.main == null)
            return false;

        if (!force && Camera.main.transform.position.y < 0.8f)
            return false;

        NeuroCueWorldMenuAnchor anchor = MainMenuCanvas.GetComponent<NeuroCueWorldMenuAnchor>();
        if (anchor == null)
            anchor = MainMenuCanvas.AddComponent<NeuroCueWorldMenuAnchor>();

        CaptureStartMenuPlaceFromCamera();
        MainMenuCanvas.transform.SetParent(null, true);
        MainMenuCanvas.SetActive(true);
        ApplyStartMenuWorldPose();
        MainMenuCanvas.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);

        anchor.DistanceFromHead = startMenuDistanceMeters;
        return true;
    }

    private void SetGameplayHudVisible(bool visible)
    {
        if (GameplayScreen != null)
            GameplayScreen.SetActive(visible);

        // Scores/timer are display-only — never block world-menu Exit rays.
        if (visible)
            SetGameplayHudRaycastsEnabled(false);
    }

    private void SetGameplayHudRaycastsEnabled(bool enabled)
    {
        if (GameplayScreen == null)
            return;

        CanvasGroup group = GameplayScreen.GetComponent<CanvasGroup>();
        if (group == null)
            group = GameplayScreen.AddComponent<CanvasGroup>();

        group.blocksRaycasts = enabled;
        group.interactable = enabled;

        GraphicRaycaster[] raycasters = GameplayScreen.GetComponents<GraphicRaycaster>();
        for (int i = 0; i < raycasters.Length; i++)
        {
            if (raycasters[i] != null)
                raycasters[i].enabled = enabled;
        }

        // TrackedDeviceGraphicRaycaster (XR) lives on the same HUD object.
        Behaviour[] behaviours = GameplayScreen.GetComponents<Behaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            if (behaviour.GetType().Name == "TrackedDeviceGraphicRaycaster")
                behaviour.enabled = enabled;
        }

        // Belt-and-suspenders: individual graphics on the HUD also stop eating hits.
        Graphic[] graphics = GameplayScreen.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
                graphics[i].raycastTarget = enabled;
        }
    }

    private void ShowGameplayExitInFrontOfUser()
    {
        EnsureGameplayExitCanvas();
        if (gameplayExitCanvas == null)
            return;

        if (MainMenuCanvas == null)
            EnsureUiReferences();

        CopyGameplayExitCanvasFromMainMenu();
        CopyIsdkRayInteractionFromMainMenu();
        FitGameplayExitCanvasToButton();

        gameplayExitCanvas.SetActive(true);
        if (ExitButton != null)
            ExitButton.SetActive(true);

        NeuroCueXrCanvasBootstrap.EnsureWorldSpaceCanvases();
    }

    private void CopyGameplayExitCanvasFromMainMenu()
    {
        if (gameplayExitCanvas == null || MainMenuCanvas == null)
            return;

        Transform source = MainMenuCanvas.transform;
        gameplayExitCanvas.transform.SetParent(null, true);
        gameplayExitCanvas.transform.SetPositionAndRotation(source.position, source.rotation);
        gameplayExitCanvas.transform.localScale = source.lossyScale;

        Canvas sourceCanvas = MainMenuCanvas.GetComponent<Canvas>();
        Canvas destCanvas = gameplayExitCanvas.GetComponent<Canvas>();
        if (sourceCanvas != null && destCanvas != null)
        {
            destCanvas.renderMode = RenderMode.WorldSpace;
            destCanvas.worldCamera = sourceCanvas.worldCamera != null ? sourceCanvas.worldCamera : Camera.main;
            destCanvas.pixelPerfect = sourceCanvas.pixelPerfect;
            destCanvas.overrideSorting = sourceCanvas.overrideSorting;
            destCanvas.sortingOrder = sourceCanvas.sortingOrder;
            destCanvas.additionalShaderChannels = sourceCanvas.additionalShaderChannels;
            destCanvas.vertexColorAlwaysGammaSpace = sourceCanvas.vertexColorAlwaysGammaSpace;
        }

        GraphicRaycaster sourceRaycaster = MainMenuCanvas.GetComponent<GraphicRaycaster>();
        GraphicRaycaster destRaycaster = gameplayExitCanvas.GetComponent<GraphicRaycaster>();
        if (sourceRaycaster != null && destRaycaster != null)
        {
            destRaycaster.ignoreReversedGraphics = sourceRaycaster.ignoreReversedGraphics;
            destRaycaster.blockingObjects = sourceRaycaster.blockingObjects;
            destRaycaster.blockingMask = sourceRaycaster.blockingMask;
        }

        gameplayExitCanvas.layer = MainMenuCanvas.layer;
    }

    private void CopyIsdkRayInteractionFromMainMenu()
    {
        if (gameplayExitCanvas == null || MainMenuCanvas == null)
            return;

        if (gameplayExitCanvas.transform.Find("ISDK_RayCanvasInteraction") != null)
            return;

        Transform sourceRay = MainMenuCanvas.transform.Find("ISDK_RayCanvasInteraction");
        if (sourceRay == null)
            return;

        GameObject clone = Instantiate(sourceRay.gameObject, gameplayExitCanvas.transform, false);
        clone.name = "ISDK_RayCanvasInteraction";
        clone.SetActive(true);
        RewireSerializedCanvasReferences(clone, gameplayExitCanvas.GetComponent<Canvas>());
        ResizeIsdkSurface(clone, GameplayExitButtonSize);
    }

    private static void ResizeIsdkSurface(GameObject isdkRoot, Vector2 canvasSize)
    {
        if (isdkRoot == null)
            return;

        MonoBehaviour[] behaviours = isdkRoot.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            System.Reflection.FieldInfo sizeField = behaviour.GetType().GetField(
                "_size",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            if (sizeField == null || sizeField.FieldType != typeof(Vector3))
                continue;

            Vector3 current = (Vector3)sizeField.GetValue(behaviour);
            float depth = current.z > 0f ? current.z : 0.01f;
            sizeField.SetValue(behaviour, new Vector3(canvasSize.x, canvasSize.y, depth));
        }
    }

    private static void RewireSerializedCanvasReferences(GameObject root, Canvas canvas)
    {
        if (root == null || canvas == null)
            return;

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            System.Reflection.FieldInfo[] fields = behaviour.GetType().GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            for (int f = 0; f < fields.Length; f++)
            {
                if (fields[f].FieldType == typeof(Canvas))
                    fields[f].SetValue(behaviour, canvas);
            }
        }
    }

    private void FitGameplayExitCanvasToButton()
    {
        if (gameplayExitCanvas == null)
            return;

        Transform leftoverPlate = gameplayExitCanvas.transform.Find("Exit Plate");
        if (leftoverPlate != null)
            DestroyImmediate(leftoverPlate.gameObject);

        Transform menuPanel = gameplayExitCanvas.transform.Find("Menu Panel");
        if (menuPanel != null)
            DestroyImmediate(menuPanel.gameObject);

        Transform startExit = FindStartMenuExit();
        Vector2 size = GameplayExitButtonSize;
        if (startExit != null)
            size = startExit.GetComponent<RectTransform>().sizeDelta;

        Vector3 worldScale = MainMenuCanvas != null
            ? MainMenuCanvas.transform.lossyScale
            : new Vector3(0.001f, 0.001f, 0.001f);

        CaptureGameplayExitPlaceFromCamera();

        gameplayExitCanvas.transform.SetParent(null, true);

        RectTransform canvasRect = gameplayExitCanvas.GetComponent<RectTransform>();
        canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
        canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        canvasRect.anchoredPosition = Vector2.zero;
        canvasRect.sizeDelta = size;
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one;

        ApplyGameplayExitWorldPose();
        gameplayExitCanvas.transform.localScale = worldScale;

        Transform exitTransform = gameplayExitCanvas.transform.Find("Exit");
        GameObject exitObject = exitTransform != null ? exitTransform.gameObject : CreateUiPanel(
            gameplayExitCanvas.transform,
            "Exit",
            Vector2.zero,
            size,
            ExitButtonColor);

        ApplyCenteredRect(exitObject.GetComponent<RectTransform>(), Vector2.zero, size);

        Image exitImage = exitObject.GetComponent<Image>();
        if (exitImage == null)
            exitImage = exitObject.AddComponent<Image>();
        exitImage.color = ExitButtonColor;
        exitImage.raycastTarget = true;

        Button button = exitObject.GetComponent<Button>();
        if (button == null)
            button = exitObject.AddComponent<Button>();
        button.targetGraphic = exitImage;
        button.interactable = true;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(ExitToStartMenu);

        EnsureExitLabel(exitObject);
        ExitButton = exitObject;
        ExitButton.SetActive(true);

        Transform isdk = gameplayExitCanvas.transform.Find("ISDK_RayCanvasInteraction");
        if (isdk != null)
            ResizeIsdkSurface(isdk.gameObject, size);
    }

    private static GameObject CreateUiPanel(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.layer = 5;
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        ApplyCenteredRect(rect, anchoredPosition, size);

        Image image = panel.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return panel;
    }

    private static void ApplyCenteredRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static void EnsureExitLabel(GameObject exitObject)
    {
        Transform existing = exitObject.transform.Find("Label");
        TextMeshProUGUI label = existing != null
            ? existing.GetComponent<TextMeshProUGUI>()
            : exitObject.GetComponentInChildren<TextMeshProUGUI>(true);

        if (label == null)
        {
            GameObject labelObject = new GameObject("Label");
            labelObject.layer = 5;
            labelObject.transform.SetParent(exitObject.transform, false);
            RectTransform labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label = labelObject.AddComponent<TextMeshProUGUI>();
        }

        label.text = "Exit";
        label.fontSize = 26f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    private void SetGameplayExitCanvasVisible(bool visible)
    {
        if (gameplayExitCanvas != null)
            gameplayExitCanvas.SetActive(visible);

        if (!visible && ExitButton != null &&
            gameplayExitCanvas != null &&
            ExitButton.transform.IsChildOf(gameplayExitCanvas.transform))
            ExitButton.SetActive(false);
    }

    private void EnsureGameplayExitCanvas()
    {
        if (gameplayExitCanvas != null)
            return;

        GameObject existing = GameObject.Find("Gameplay Exit Canvas");
        if (existing != null)
            DestroyImmediate(existing);

        gameplayExitCanvas = new GameObject("Gameplay Exit Canvas");
        gameplayExitCanvas.layer = MainMenuCanvas != null ? MainMenuCanvas.layer : 5;

        RectTransform canvasRect = gameplayExitCanvas.AddComponent<RectTransform>();
        canvasRect.sizeDelta = GameplayExitButtonSize;
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.zero;

        Canvas canvas = gameplayExitCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;

        CanvasScaler scaler = gameplayExitCanvas.AddComponent<CanvasScaler>();
        CanvasScaler sourceScaler = MainMenuCanvas != null ? MainMenuCanvas.GetComponent<CanvasScaler>() : null;
        if (sourceScaler != null)
        {
            scaler.uiScaleMode = sourceScaler.uiScaleMode;
            scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
            scaler.dynamicPixelsPerUnit = sourceScaler.dynamicPixelsPerUnit;
        }

        GraphicRaycaster graphicRaycaster = gameplayExitCanvas.AddComponent<GraphicRaycaster>();
        GraphicRaycaster sourceRaycaster = MainMenuCanvas != null ? MainMenuCanvas.GetComponent<GraphicRaycaster>() : null;
        if (sourceRaycaster != null)
        {
            graphicRaycaster.ignoreReversedGraphics = sourceRaycaster.ignoreReversedGraphics;
            graphicRaycaster.blockingObjects = sourceRaycaster.blockingObjects;
            graphicRaycaster.blockingMask = sourceRaycaster.blockingMask;
        }
        else
        {
            graphicRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            graphicRaycaster.ignoreReversedGraphics = true;
        }
    }

    private void EnsureUiReferences()
    {
        if (MainMenuCanvas == null)
        {
            GameObject mainMenu = GameObject.Find("Main Menu Canvas");
            if (mainMenu != null)
                MainMenuCanvas = mainMenu;
        }

        if (StartScreen == null && MainMenuCanvas != null)
        {
            Transform start = MainMenuCanvas.transform.Find("Start Screen");
            if (start != null)
                StartScreen = start.gameObject;
        }

        if (WorldGameplayPanel == null && MainMenuCanvas != null)
        {
            Transform worldPanel = MainMenuCanvas.transform.Find("Gameplay Screen");
            if (worldPanel != null)
                WorldGameplayPanel = worldPanel.gameObject;
        }

        if (ExitButton == null && WorldGameplayPanel != null)
        {
            Transform exit = WorldGameplayPanel.transform.Find("Exit");
            if (exit != null)
                ExitButton = exit.gameObject;
        }

        if (ResultScreen == null && WorldGameplayPanel != null)
        {
            Transform results = WorldGameplayPanel.transform.Find("Results Screen");
            if (results != null)
                ResultScreen = results.gameObject;
        }

        // Head-locked HUD only — never overwrite with the world "Gameplay Screen".
        if (GameplayScreen == null || GameplayScreen.name == "Gameplay Screen")
        {
            GameObject gameplayHud = GameObject.Find("Gameplay");
            if (gameplayHud == null)
            {
                // Find() skips inactive objects; HUD starts inactive.
                Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform t = transforms[i];
                    if (t != null && t.name == "Gameplay" && t.gameObject.scene.IsValid())
                    {
                        gameplayHud = t.gameObject;
                        break;
                    }
                }
            }

            if (gameplayHud != null)
                GameplayScreen = gameplayHud;
        }
    }

    private void ApplyHoldDurationSetting()
    {
        CueHoldDuration = UseTwoSecondHold ? 2f : 1f;
    }

    private void ResetSessionScores()
    {
        leftScore = 0;
        rightScore = 0;
        correctHits = 0;
        wrongHits = 0;
        cueCount = 0;
        cuesSinceLastHold = 0;
        CurrentLevel = 1;
        RequireLeftHand = true;
        hasSpawnedFirstCue = false;
        repeatHoldCueAfterMiss = false;
        levelAdvancePending = false;
        ResetAssessmentGrid();
        RefillBalancedSpawnPlan();
    }

    private bool IsAssessmentGridV2Active()
    {
        return UseAssessmentGridV2 &&
               string.Equals(AssessmentOrTrainingMode, "assessment", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(AssessmentProtocolVersion, AssessmentGridV2ProtocolVersion, StringComparison.OrdinalIgnoreCase);
    }

    private void ResetAssessmentGrid()
    {
        assessmentGridSequence.Clear();
        assessmentGridSequenceIndex = 0;
        assessmentGridRepetitionNumber = 1;
        currentRandomizationOrder = 0;
        hasCurrentAssessmentGridCell = false;

        if (IsAssessmentGridV2Active())
            BuildNewAssessmentGridTraversal();
    }

    private bool BuildNewAssessmentGridTraversal()
    {
        assessmentGridSequence.Clear();
        assessmentGridSequenceIndex = 0;
        hasCurrentAssessmentGridCell = false;

        ValidateAssessmentGridSettings();

        for (int row = 0; row < AssessmentGridYPositionsM.Length; row++)
        {
            for (int column = 0; column < AssessmentGridXPositionsM.Length; column++)
            {
                float x = AssessmentGridXPositionsM[column];
                assessmentGridSequence.Add(new AssessmentGridCell
                {
                    Row = row,
                    Column = column,
                    IntendedX = x,
                    IntendedY = AssessmentGridYPositionsM[row],
                    IntendedZ = AssessmentGridV2DepthM,
                    Hemispace = GetCueHemispaceFromPatientX(x)
                });
            }
        }

        Shuffle(assessmentGridSequence);
        ValidateAssessmentGridTraversal();
        return true;
    }

    private bool TrySelectNextAssessmentGridCell(int requiredArmSide)
    {
        if (!IsAssessmentGridV2Active())
            return false;

        if (assessmentGridSequence.Count == 0 && !BuildNewAssessmentGridTraversal())
            return false;

        if (assessmentGridSequenceIndex >= assessmentGridSequence.Count)
        {
            assessmentGridRepetitionNumber++;
            if (!BuildNewAssessmentGridTraversal())
                return false;
        }

        currentAssessmentGridCell = assessmentGridSequence[assessmentGridSequenceIndex];
        assessmentGridSequenceIndex++;
        currentRandomizationOrder = assessmentGridSequenceIndex;
        hasCurrentAssessmentGridCell = true;

        currentCueLocation = new NeuroCueLocationResult
        {
            Hemispace = currentAssessmentGridCell.Hemispace == 0 ? NeuroCueSide.Left : NeuroCueSide.Right,
            CrossesMidline = currentAssessmentGridCell.CrossesMidlineForHand(requiredArmSide),
            SideOffsetMeters = Mathf.Abs(currentAssessmentGridCell.IntendedX)
        };

        AssessmentRepetitionNumber = assessmentGridRepetitionNumber;
        AssessmentGridRow = currentAssessmentGridCell.Row;
        AssessmentGridColumn = currentAssessmentGridCell.Column;
        return true;
    }

    private static void ValidateAssessmentGridSettings()
    {
        if (AssessmentGridXPositionsM == null || AssessmentGridXPositionsM.Length != 12)
            throw new Exception("NeuroTap assessment grid must contain exactly 12 horizontal positions.");

        bool hasNegative = false;
        bool hasPositive = false;
        float closestLeft = float.NegativeInfinity;
        float closestRight = float.PositiveInfinity;

        for (int i = 0; i < AssessmentGridXPositionsM.Length; i++)
        {
            float x = AssessmentGridXPositionsM[i];

            if (Mathf.Abs(x) < AssessmentGridEpsilon)
                throw new Exception("NeuroTap assessment grid must not contain an X=0 midline cue.");

            if (i > 0 && AssessmentGridXPositionsM[i] <= AssessmentGridXPositionsM[i - 1])
                throw new Exception("NeuroTap assessment grid X positions must be strictly ordered from left to right.");

            if (x < 0f)
            {
                hasNegative = true;
                closestLeft = Mathf.Max(closestLeft, x);
            }
            else if (x > 0f)
            {
                hasPositive = true;
                closestRight = Mathf.Min(closestRight, x);
            }
        }

        if (!hasNegative)
            throw new Exception("Assessment grid missing left hemispace.");
        if (!hasPositive)
            throw new Exception("Assessment grid missing right hemispace.");
        if (Mathf.Abs(closestLeft - (-0.05f)) > AssessmentGridEpsilon)
            throw new Exception("Closest left cue must be -0.05 m.");
        if (Mathf.Abs(closestRight - 0.05f) > AssessmentGridEpsilon)
            throw new Exception("Closest right cue must be +0.05 m.");
        if (AssessmentGridYPositionsM == null || AssessmentGridYPositionsM.Length == 0)
            throw new Exception("Assessment grid missing vertical positions.");

        for (int i = 1; i < AssessmentGridYPositionsM.Length; i++)
        {
            if (AssessmentGridYPositionsM[i] <= AssessmentGridYPositionsM[i - 1])
                throw new Exception("NeuroTap assessment grid Y positions must be strictly ordered from inferior to superior.");
        }
    }

    private void ValidateAssessmentGridTraversal()
    {
        bool[] columnsSeen = new bool[AssessmentGridXPositionsM.Length];

        for (int i = 0; i < assessmentGridSequence.Count; i++)
        {
            AssessmentGridCell cell = assessmentGridSequence[i];

            if (cell.Column < 0 || cell.Column >= AssessmentGridXPositionsM.Length)
                throw new Exception("assessment_grid_column is outside the valid v2 range.");

            if (cell.Row < 0 || cell.Row >= AssessmentGridYPositionsM.Length)
                throw new Exception("assessment_grid_row is outside the valid v2 range.");

            if (Mathf.Abs(cell.IntendedX - AssessmentGridXPositionsM[cell.Column]) > AssessmentGridEpsilon)
                throw new Exception("assessment_grid_column does not match intended X position.");

            if (Mathf.Abs(cell.IntendedY - AssessmentGridYPositionsM[cell.Row]) > AssessmentGridEpsilon)
                throw new Exception("assessment_grid_row does not match intended Y position.");

            columnsSeen[cell.Column] = true;
        }

        for (int column = 0; column < columnsSeen.Length; column++)
        {
            if (!columnsSeen[column])
                throw new Exception("Assessment grid traversal missing column " + column + ".");
        }
    }

    private void RefillBalancedSpawnPlan()
    {
        RefillHeightQueue();
        requiredHandScheduler.Reset(RequiredHandSchedule);
        cueLocationScheduler.Reset(CueLocationSchedule);
    }

    private void RefillHeightQueue()
    {
        pendingHeightOffsets.Clear();

        float midHeight = (CueMinHeightRelativeToEyes + CueMaxHeightRelativeToEyes) * 0.5f;
        for (int i = 0; i < 6; i++)
            pendingHeightOffsets.Add(UnityEngine.Random.Range(CueMinHeightRelativeToEyes, midHeight));
        for (int i = 0; i < 6; i++)
            pendingHeightOffsets.Add(UnityEngine.Random.Range(midHeight, CueMaxHeightRelativeToEyes));
        Shuffle(pendingHeightOffsets);
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    private void ApplyBalancedCueAssignment(bool preserveArmSide)
    {
        // On a missed hold cue, keep the same required hand and do not consume the queue.
        if (preserveArmSide)
            return;

        NeuroCueSide nextHand = requiredHandScheduler.Next(CurrentLevel);
        RequireLeftHand = nextHand == NeuroCueSide.Left;
    }

    private void AssignCueLocation()
    {
        NeuroCueSide requiredHand = RequireLeftHand ? NeuroCueSide.Left : NeuroCueSide.Right;
        if (TrySelectNextAssessmentGridCell(requiredHand == NeuroCueSide.Left ? 0 : 1))
            return;

        currentCueLocation = cueLocationScheduler.Next(requiredHand, CurrentLevel);
    }

    private void LockSessionPlayArea()
    {
        if (!LockPlayAreaAtSessionStart || Camera.main == null)
        {
            sessionPlayAreaLocked = false;
            return;
        }

        Transform cameraTransform = Camera.main.transform;

        // Lock origin in room space so walking toward cues does not push the next cues farther away.
        sessionPlayOrigin = cameraTransform.position;

        sessionPlayForward = cameraTransform.forward;
        sessionPlayForward.y = 0f;
        if (sessionPlayForward.sqrMagnitude < 0.0001f)
            sessionPlayForward = Vector3.forward;
        sessionPlayForward.Normalize();

        sessionPlayRight = cameraTransform.right;
        sessionPlayRight.y = 0f;
        if (sessionPlayRight.sqrMagnitude < 0.0001f)
            sessionPlayRight = Vector3.right;
        sessionPlayRight.Normalize();

        sessionPlayAreaLocked = true;
    }

    private void WireStartMenuButtons()
    {
        if (StartScreen == null)
            return;

        Button[] buttons = StartScreen.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
                continue;

            string n = button.gameObject.name;
            bool isPlay =
                n.IndexOf("Play", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                n.IndexOf("Again", System.StringComparison.OrdinalIgnoreCase) < 0;

            if (!isPlay)
                continue;

            button.onClick.RemoveListener(StartGame);
            button.onClick.AddListener(StartGame);
        }
    }

    private void EnsureStartMenu()
    {
        if (StartScreen != null || !CreateRuntimeStartMenu)
            return;

        EnsureUiReferences();

        Transform menuParent = MainMenuCanvas != null
            ? MainMenuCanvas.transform
            : null;

        if (menuParent == null)
            return;

        StartScreen = new GameObject("Start Screen");
        StartScreen.layer = 5;
        StartScreen.transform.SetParent(menuParent, false);

        RectTransform panel = StartScreen.AddComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(560f, 320f);
        panel.anchoredPosition = Vector2.zero;

        Image panelImage = StartScreen.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.72f);

        GameObject titleObject = new GameObject("Title");
        titleObject.transform.SetParent(StartScreen.transform, false);
        RectTransform titleRect = titleObject.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(700f, 80f);
        titleRect.anchoredPosition = new Vector2(0f, 120f);
        TextMeshProUGUI titleText = titleObject.AddComponent<TextMeshProUGUI>();
        titleText.text = "NeuroCue";
        titleText.fontSize = 48f;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;

        GameObject playButtonObject = new GameObject("Play Button");
        playButtonObject.layer = 5;
        playButtonObject.transform.SetParent(StartScreen.transform, false);
        RectTransform playRect = playButtonObject.AddComponent<RectTransform>();
        playRect.anchorMin = new Vector2(0.5f, 0.5f);
        playRect.anchorMax = new Vector2(0.5f, 0.5f);
        playRect.sizeDelta = new Vector2(320f, 64f);
        playRect.anchoredPosition = new Vector2(0f, 10f);

        Image playImage = playButtonObject.AddComponent<Image>();
        playImage.color = new Color(0.2f, 0.55f, 0.95f, 1f);
        playImage.raycastTarget = true;

        Button playButton = playButtonObject.AddComponent<Button>();
        playButton.targetGraphic = playImage;
        playButton.onClick.AddListener(StartGame);

        GameObject playLabelObject = new GameObject("Label");
        playLabelObject.transform.SetParent(playButtonObject.transform, false);
        RectTransform playLabelRect = playLabelObject.AddComponent<RectTransform>();
        playLabelRect.anchorMin = Vector2.zero;
        playLabelRect.anchorMax = Vector2.one;
        playLabelRect.offsetMin = Vector2.zero;
        playLabelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI playLabel = playLabelObject.AddComponent<TextMeshProUGUI>();
        playLabel.text = "Play";
        playLabel.fontSize = 30f;
        playLabel.alignment = TextAlignmentOptions.Center;
        playLabel.color = Color.white;

        GameObject toggleObject = new GameObject("Hold Duration Toggle");
        toggleObject.layer = 5;
        toggleObject.transform.SetParent(StartScreen.transform, false);
        RectTransform toggleRect = toggleObject.AddComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(0.5f, 0.5f);
        toggleRect.anchorMax = new Vector2(0.5f, 0.5f);
        toggleRect.sizeDelta = new Vector2(420f, 40f);
        toggleRect.anchoredPosition = new Vector2(0f, -80f);

        holdDurationToggle = toggleObject.AddComponent<Toggle>();
        holdDurationToggle.isOn = UseTwoSecondHold;
        holdDurationToggle.onValueChanged.AddListener(SetUseTwoSecondHold);

        GameObject toggleBackground = new GameObject("Background");
        toggleBackground.transform.SetParent(toggleObject.transform, false);
        RectTransform toggleBackgroundRect = toggleBackground.AddComponent<RectTransform>();
        toggleBackgroundRect.anchorMin = new Vector2(0f, 0.5f);
        toggleBackgroundRect.anchorMax = new Vector2(0f, 0.5f);
        toggleBackgroundRect.sizeDelta = new Vector2(28f, 28f);
        toggleBackgroundRect.anchoredPosition = new Vector2(14f, 0f);
        Image toggleBackgroundImage = toggleBackground.AddComponent<Image>();
        toggleBackgroundImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        GameObject toggleCheckmark = new GameObject("Checkmark");
        toggleCheckmark.transform.SetParent(toggleBackground.transform, false);
        RectTransform toggleCheckmarkRect = toggleCheckmark.AddComponent<RectTransform>();
        toggleCheckmarkRect.anchorMin = Vector2.zero;
        toggleCheckmarkRect.anchorMax = Vector2.one;
        toggleCheckmarkRect.offsetMin = new Vector2(6f, 6f);
        toggleCheckmarkRect.offsetMax = new Vector2(-6f, -6f);
        Image toggleCheckmarkImage = toggleCheckmark.AddComponent<Image>();
        toggleCheckmarkImage.color = new Color(0.2f, 0.85f, 0.35f, 1f);

        holdDurationToggle.targetGraphic = toggleBackgroundImage;
        holdDurationToggle.graphic = toggleCheckmarkImage;

        GameObject toggleLabelObject = new GameObject("Label");
        toggleLabelObject.transform.SetParent(toggleObject.transform, false);
        RectTransform toggleLabelRect = toggleLabelObject.AddComponent<RectTransform>();
        toggleLabelRect.anchorMin = new Vector2(0f, 0f);
        toggleLabelRect.anchorMax = new Vector2(1f, 1f);
        toggleLabelRect.offsetMin = new Vector2(44f, 0f);
        toggleLabelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI toggleLabel = toggleLabelObject.AddComponent<TextMeshProUGUI>();
        toggleLabel.text = "Use 2 second hold (default: 1 second)";
        toggleLabel.fontSize = 22f;
        toggleLabel.alignment = TextAlignmentOptions.MidlineLeft;
        toggleLabel.color = Color.white;
    }
#if UNITY_EDITOR
    private void HandleEditorKeyboardStartMenu()
    {
        if (!EnableEditorKeyboardStart || !IsStartMenuVisible())
            return;

        if (WasKeyPressedThisFrame(KeyCode.Return) ||
            WasKeyPressedThisFrame(KeyCode.KeypadEnter) ||
            WasKeyPressedThisFrame(KeyCode.Space))
        {
            StartGame();
            return;
        }

        if (WasKeyPressedThisFrame(KeyCode.H))
        {
            SetUseTwoSecondHold(!UseTwoSecondHold);

            if (holdDurationToggle != null)
                holdDurationToggle.isOn = UseTwoSecondHold;
        }
    }

    private bool IsStartMenuVisible()
    {
        if (gameplaySessionStarted)
            return false;

        if (ResultScreen != null && ResultScreen.activeSelf)
            return false;

        if (MainMenuCanvas != null && MainMenuCanvas.activeInHierarchy)
            return true;

        return StartScreen == null || StartScreen.activeInHierarchy;
    }
#endif

    /// <summary>
    /// Safe keyboard poll for both the old Input Manager and the Input System package.
    /// </summary>
    private static bool WasKeyPressedThisFrame(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            switch (key)
            {
                case KeyCode.Escape:
                    return keyboard.escapeKey.wasPressedThisFrame;
                case KeyCode.Return:
                    return keyboard.enterKey.wasPressedThisFrame;
                case KeyCode.KeypadEnter:
                    return keyboard.numpadEnterKey.wasPressedThisFrame;
                case KeyCode.Space:
                    return keyboard.spaceKey.wasPressedThisFrame;
                case KeyCode.H:
                    return keyboard.hKey.wasPressedThisFrame;
            }
        }
#endif
        try
        {
            return Input.GetKeyDown(key);
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }

    public void MarkHoldCueMissed()
    {
        // Repeat the same hold cue / hand until completed (keeps 50/50 hand split intact).
        repeatHoldCueAfterMiss = true;
    }
    private IEnumerator SpawnFirstTargetWhenCameraReady()
    {
        float waitTime = 0f;

        while (!IsCameraReadyForCuePlacement() && waitTime < firstCueTrackingTimeout)
        {
            waitTime += Time.unscaledDeltaTime;
            yield return null;
        }

        // One extra frame so tracking position is stable before locking the play area.
        yield return null;

        if (LockPlayAreaAtSessionStart)
            LockSessionPlayArea();

        SpawnTarget();
    }

    private bool IsCameraReadyForCuePlacement()
    {
        if (Camera.main == null)
            return false;

        float minHeight = UnityTestingMode ? 0.5f : minCameraHeightForFirstCue;
        return Camera.main.transform.position.y >= minHeight;
    }
    private void Update()
    {
        if (WasKeyPressedThisFrame(KeyCode.Escape))
            ExitGame();

#if UNITY_EDITOR
        HandleEditorKeyboardStartMenu();
#endif

        if (!gameplaySessionStarted)
            return;

        sessionActive = true;
        GameRunning = true;

        if (wrongHitCooldown > 0f)
            wrongHitCooldown -= Time.unscaledDeltaTime;

        if (KeepCueFacingCameraAfterSpawn && currentTarget != null)
            FaceTargetToCamera(currentTarget.transform);

        UpdatePromptVisibility();
    }

    private void CaptureStartMenuPlaceFromCamera()
    {
        CapturePlaceFromCamera(out startMenuPlaceOrigin, out startMenuPlaceForward, out startMenuPlaceEyeHeight);
        startMenuPlaceCaptured = startMenuPlaceForward.sqrMagnitude > 0.0001f;
    }

    private void ApplyStartMenuWorldPose()
    {
        if (MainMenuCanvas == null || !MainMenuCanvas.activeInHierarchy || !startMenuPlaceCaptured)
            return;

        Vector3 position = startMenuPlaceOrigin + startMenuPlaceForward * startMenuDistanceMeters;
        position.y = startMenuHeightMeters > 0.01f ? startMenuHeightMeters : startMenuPlaceEyeHeight;
        MainMenuCanvas.transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(startMenuPlaceForward, Vector3.up));
    }

    private static void CapturePlaceFromCamera(out Vector3 origin, out Vector3 forward, out float eyeHeight)
    {
        origin = Vector3.zero;
        forward = Vector3.forward;
        eyeHeight = 1.5f;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Vector3 cameraForward = camera.transform.forward;
        cameraForward.y = 0f;
        if (cameraForward.sqrMagnitude < 0.0001f)
            cameraForward = Vector3.forward;

        forward = cameraForward.normalized;
        Vector3 cameraPosition = camera.transform.position;
        origin = new Vector3(cameraPosition.x, 0f, cameraPosition.z);
        eyeHeight = Mathf.Max(cameraPosition.y, 1.5f);
    }

    private void CaptureGameplayExitPlaceFromCamera()
    {
        CapturePlaceFromCamera(out gameplayExitPlaceOrigin, out gameplayExitPlaceForward, out _);
    }

    private void ApplyGameplayExitWorldPose()
    {
        if (gameplayExitCanvas == null || !gameplayExitCanvas.activeInHierarchy)
            return;

        Vector3 position = gameplayExitPlaceOrigin + gameplayExitPlaceForward * gameplayExitDistanceMeters;
        position.y = gameplayExitHeightMeters;
        gameplayExitCanvas.transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(gameplayExitPlaceForward, Vector3.up));
    }

    private void LateUpdate()
    {
        ApplyStartMenuWorldPose();
        ApplyGameplayExitWorldPose();

        // Video showed scores/cues advancing while the HUD stayed frozen at 01:00.
        // Drive the clock from the visible gameplay HUD so a cleared flag cannot freeze it.
        bool hudUp = GameplayScreen != null && GameplayScreen.activeInHierarchy;
        bool resultsUp = ResultScreen != null && ResultScreen.activeSelf;
        if (!hudUp || resultsUp || levelAdvancePending)
            return;

        if (!gameplaySessionStarted)
        {
            // Recover: HUD is in gameplay mode but the session flag was cleared.
            gameplaySessionStarted = true;
            if (sessionEndsAtUnscaled <= Time.unscaledTime)
                sessionEndsAtUnscaled = Time.unscaledTime + Mathf.Max(1f, sessionDuration);
        }

        sessionActive = true;
        GameRunning = true;

        TimeRemaining = sessionEndsAtUnscaled - Time.unscaledTime;
        if (TimeRemaining < 0f)
            TimeRemaining = 0f;

        RefreshTimerUi();

        if (TimeRemaining <= 0f)
            HandleSessionTimerExpired();
    }

    private void BeginSessionTimer(float seconds)
    {
        sessionDuration = Mathf.Max(1f, seconds);
        sessionEndsAtUnscaled = Time.unscaledTime + sessionDuration;
        sessionActive = true;
        GameRunning = true;
        gameplaySessionStarted = true;
        levelAdvancePending = false;
        TimeRemaining = sessionDuration;

        EnsureSessionClock();
        ResolveTimerUiReferences();
        if (sessionClock != null)
        {
            sessionClock.BindUi(GameplayScreen, TimerText, TimerText2, LevelText, TimerFillImage);
            sessionClock.SetLevel(CurrentLevel);
            sessionClock.StartCountdown(sessionDuration, null);
        }

        RefreshTimerUi();
    }

    private void StopSessionTimer()
    {
        sessionActive = false;
        GameRunning = false;

        if (sessionClock != null)
            sessionClock.StopCountdown();
    }

    private void SetStartScreenInteractable(bool interactable)
    {
        if (StartScreen == null)
            return;

        CanvasGroup group = StartScreen.GetComponent<CanvasGroup>();
        if (group == null)
            group = StartScreen.AddComponent<CanvasGroup>();

        group.interactable = interactable;
        group.blocksRaycasts = interactable;

        Button[] buttons = StartScreen.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null)
                buttons[i].interactable = interactable;
        }
    }

    private void ResolveTimerUiReferences()
    {
        if (GameplayScreen == null)
            EnsureUiReferences();

        if (GameplayScreen == null)
            return;

        if (TimerText == null)
        {
            Transform timerText = FindChildRecursive(GameplayScreen.transform, "Timer Text");
            if (timerText != null)
                TimerText = timerText.GetComponent<TMP_Text>();
        }

        if (TimerText2 == null)
        {
            Transform timer = FindChildRecursive(GameplayScreen.transform, "Timer");
            if (timer != null)
                TimerText2 = timer.GetComponent<TMP_Text>();
        }

        if (LevelText == null)
        {
            Transform level = FindChildRecursive(GameplayScreen.transform, "Level Text");
            if (level == null)
                level = FindChildRecursive(GameplayScreen.transform, "Level");
            if (level != null)
                LevelText = level.GetComponent<TMP_Text>();
        }

        if (TimerFillImage == null)
        {
            Image[] images = GameplayScreen.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].type == Image.Type.Filled)
                {
                    TimerFillImage = images[i];
                    break;
                }
            }
        }
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name)
                return child;

            Transform nested = FindChildRecursive(child, name);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private void RefreshTimerUi()
    {
        float displayTime = Mathf.Max(0f, TimeRemaining);
        int seconds = displayTime <= 0f ? 0 : Mathf.CeilToInt(displayTime);
        string clock = $"{seconds / 60:00}:{seconds % 60:00}";

        if (TimerText != null) TimerText.text = clock;
        if (TimerText2 != null) TimerText2.text = clock;

        // Also write by name in case inspector refs drift from the live HUD instance.
        if (GameplayScreen != null)
        {
            TMP_Text[] texts = GameplayScreen.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null)
                    continue;

                string n = text.gameObject.name;
                if (n == "Timer Text" || n == "Timer")
                    text.text = clock;
                else if (n == "Level Text" || n == "Level")
                    text.text = "LEVEL " + CurrentLevel;
            }
        }

        if (TimerFillImage != null) TimerFillImage.fillAmount = Mathf.Clamp01(displayTime / Mathf.Max(0.01f, sessionDuration));
        if (LevelText != null) LevelText.text = "LEVEL " + CurrentLevel;
    }

    private void HandleSessionTimerExpired()
    {
        if (!gameplaySessionStarted)
            return;

        levelAdvancePending = true;

        if (pendingSpawnCoroutine != null)
        {
            StopCoroutine(pendingSpawnCoroutine);
            pendingSpawnCoroutine = null;
        }

        // Don't freeze at 00:00 waiting on an active cue — clear it and advance/reset timer.
        if (currentTarget != null)
        {
            TargetInteraction active = currentTarget.GetComponent<TargetInteraction>();
            if (active != null && !active.IsFinished)
                active.ForceLogFinal("timer_expired");

            Destroy(currentTarget);
            currentTarget = null;
            activeTargetInteraction = null;
        }

        NextLevel();
    }

    public void ExitToStartMenu()
    {
        FlushActiveCueSnapshot("session_exit");
        ReturnToStartMenu();
    }

    public void ExitGame()
    {
        // Start-menu Exit still quits the application.
        FlushActiveCueSnapshot("app_exit");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator SpawnProtection()
    {
        SpawnProtectionActive = true;
        yield return new WaitForSeconds(0.3f);
        SpawnProtectionActive = false;
    }

    public bool SpawnProtectionActive { get; private set; }

    public void SpawnTarget()
    {
        Vector3 previousPos = currentTarget != null ? currentTarget.transform.position : Vector3.positiveInfinity;

        if (currentTarget != null)
        {
            TargetInteraction oldTarget = currentTarget.GetComponent<TargetInteraction>();

            if (oldTarget != null && !oldTarget.IsFinished)
                oldTarget.ForceLogFinal("replaced");

            Destroy(currentTarget);
        }

        bool preserveArmSide = repeatHoldCueAfterMiss;

        if (repeatHoldCueAfterMiss)
        {
            CurrentCueRequiresHold = true;
            repeatHoldCueAfterMiss = false;
        }
        else
        {
            CurrentCueRequiresHold = EveryCueRequiresHold;
        }

        if (!EveryCueRequiresHold)
        {
            cuesSinceLastHold++;

            if (cuesSinceLastHold >= 4 && UnityEngine.Random.value <= 0.35f)
            {
                CurrentCueRequiresHold = true;
                cuesSinceLastHold = 0;
            }
        }

        ApplyBalancedCueAssignment(preserveArmSide);
        AssignCueLocation();

        Vector3 spawnPos = GetSpawnCenter();
        bool validPosition = false;

        for (int attempts = 0; attempts < 20 && !validPosition; attempts++)
        {
            spawnPos = GetHeadsetRelativeCuePosition();

            bool farFromPrevious =
                previousPos == Vector3.positiveInfinity ||
                Vector3.Distance(spawnPos, previousPos) > MinDistanceFromPreviousCue;

            validPosition = farFromPrevious;
        }

        GameObject targetPrefab = RequireLeftHand ? RedTargetPrefab : BlueTargetPrefab;

        if (targetPrefab == null)
            return;

        currentTarget = Instantiate(targetPrefab, spawnPos, Quaternion.identity);
        hasSpawnedFirstCue = true;
        FaceTargetToCamera(currentTarget.transform);

        // Mild size reduction only — strong shrink made cues look (and feel) farther away.
        float levelScale = Mathf.Max(0.75f, Mathf.Pow(0.92f, CurrentLevel - 1));
        currentTarget.transform.localScale = targetBaseScale * levelScale;

        cueCount++;
        int cueNumber = ((cueCount - 1) % 6) + 1;
        int requiredArmSide = RequireLeftHand ? 0 : 1;
        int cueHemispace = hasCurrentAssessmentGridCell ? currentAssessmentGridCell.Hemispace : currentCueLocation.Hemispace == NeuroCueSide.Left ? 0 : 1;
        bool crossesMidline = hasCurrentAssessmentGridCell ? currentAssessmentGridCell.CrossesMidlineForHand(requiredArmSide) : currentCueLocation.CrossesMidline;
        float intendedOffsetX = hasCurrentAssessmentGridCell ? currentAssessmentGridCell.IntendedX : GetCurrentCueSideOffset();
        float intendedOffsetY = hasCurrentAssessmentGridCell ? currentAssessmentGridCell.IntendedY : Camera.main != null ? spawnPos.y - Camera.main.transform.position.y : float.NaN;
        float intendedDepth = hasCurrentAssessmentGridCell ? currentAssessmentGridCell.IntendedZ : CueDistanceFromHeadset;
        float crossingProbability = NeuroCueLocationScheduler.GetCrossingProbabilityForLevel(CueLocationSchedule, CurrentLevel);
        int assessmentRepetitionNumber = hasCurrentAssessmentGridCell ? assessmentGridRepetitionNumber : AssessmentRepetitionNumber;
        int assessmentGridRow = hasCurrentAssessmentGridCell ? currentAssessmentGridCell.Row : AssessmentGridRow;
        int assessmentGridColumn = hasCurrentAssessmentGridCell ? currentAssessmentGridCell.Column : AssessmentGridColumn;
        int randomizationOrder = hasCurrentAssessmentGridCell ? currentRandomizationOrder : cueCount;

        TargetInteraction target = currentTarget.GetComponent<TargetInteraction>();

        if (target != null)
        {
            target.InitializeCue(
                cueCount,
                cueNumber,
                CurrentLevel,
                requiredArmSide,
                cueHemispace,
                crossesMidline,
                assessmentRepetitionNumber,
                assessmentGridRow,
                assessmentGridColumn,
                randomizationOrder,
                GetRequiredHandLabel(requiredArmSide),
                GetHemispaceLabel(cueHemispace),
                GetHorizontalZone(intendedOffsetX),
                GetVerticalZone(intendedOffsetY),
                GetDepthZone(intendedDepth),
                intendedOffsetX,
                intendedOffsetY,
                intendedDepth,
                crossingProbability,
                currentCueLocation.SideOffsetMeters,
                BuildCueConditionId(requiredArmSide, cueHemispace, crossesMidline, intendedOffsetY, intendedDepth),
                CurrentCueRequiresHold,
                CueHoldDuration,
                CueLifeTime,
                sessionId);

            activeTargetInteraction = target;
        }

        promptShownAt = Time.time;
        UpdateUI();
    }

    private Vector3 GetSpawnCenter()
    {
        return GetHeadsetRelativeCuePosition();
    }
    private Vector3 GetHeadsetRelativeCuePosition()
    {
        if (Camera.main == null)
            return Vector3.forward * CueDistanceFromHeadset;

        Transform cameraTransform = Camera.main.transform;

        // Always measure distance from the current headset so cues stay in arm reach.
        // Optional locked yaw keeps left/right consistent without pushing cues farther away.
        Vector3 forward = sessionPlayAreaLocked ? sessionPlayForward : GetHorizontalForward(cameraTransform);
        Vector3 right = sessionPlayAreaLocked ? sessionPlayRight : GetHorizontalRight(cameraTransform);

        if (hasCurrentAssessmentGridCell)
        {
            Vector3 origin = sessionPlayAreaLocked ? sessionPlayOrigin : cameraTransform.position;
            Vector3 assessmentPosition = origin + forward.normalized * currentAssessmentGridCell.IntendedZ;
            assessmentPosition += right.normalized * currentAssessmentGridCell.IntendedX;
            assessmentPosition.y = origin.y + currentAssessmentGridCell.IntendedY;
            return assessmentPosition;
        }

        float sideOffset;
        float heightOffset;

        sideOffset = GetCurrentCueSideOffset();

        if (!hasSpawnedFirstCue)
            heightOffset = FirstCueHeightRelativeToEyes;
        else
        {
            if (pendingHeightOffsets.Count == 0)
                RefillHeightQueue();

            heightOffset = pendingHeightOffsets[0];
            pendingHeightOffsets.RemoveAt(0);
        }

        Vector3 eyePosition = cameraTransform.position;
        Vector3 spawnPosition = eyePosition + forward.normalized * CueDistanceFromHeadset;
        spawnPosition += right.normalized * sideOffset;
        spawnPosition.y = eyePosition.y + heightOffset;

        spawnPosition = ClampCueToReachDistance(spawnPosition, eyePosition);
        return ClampCueHeight(spawnPosition);
    }

    private float GetCurrentCueSideOffset()
    {
        float sign = currentCueLocation.Hemispace == NeuroCueSide.Left ? -1f : 1f;
        return sign * currentCueLocation.SideOffsetMeters;
    }

    private static int GetCueHemispaceFromPatientX(float patientCenteredX)
    {
        if (patientCenteredX < 0f)
            return 0;
        if (patientCenteredX > 0f)
            return 1;

        throw new Exception("Invalid assessment cue: X=0 midline cue is not permitted.");
    }

    private static string GetRequiredHandLabel(int armSide)
    {
        return armSide == 0 ? "left" : "right";
    }

    private static string GetHemispaceLabel(int hemispace)
    {
        return hemispace == 0 ? "Left" : "Right";
    }

    private static string GetHorizontalZone(float intendedOffsetX)
    {
        return intendedOffsetX < 0f ? "left" : "right";
    }

    private string GetVerticalZone(float intendedOffsetY)
    {
        if (float.IsNaN(intendedOffsetY))
            return "";

        float lowThreshold = CueMinHeightRelativeToEyes + ((CueMaxHeightRelativeToEyes - CueMinHeightRelativeToEyes) / 3f);
        float highThreshold = CueMinHeightRelativeToEyes + (2f * (CueMaxHeightRelativeToEyes - CueMinHeightRelativeToEyes) / 3f);

        if (intendedOffsetY <= lowThreshold)
            return "lower";
        if (intendedOffsetY >= highThreshold)
            return "upper";
        return "middle";
    }

    private string GetDepthZone(float intendedDepth)
    {
        if (float.IsNaN(intendedDepth))
            return "";

        if (intendedDepth < CueDistanceFromHeadset - 0.05f)
            return "near";
        if (intendedDepth > CueDistanceFromHeadset + 0.05f)
            return "far";
        return "normal";
    }

    private string BuildCueConditionId(int armSide, int hemispace, bool crossMidline, float intendedOffsetY, float intendedDepth)
    {
        return GetRequiredHandLabel(armSide) + "_hand__" +
               GetHemispaceLabel(hemispace) + "_space__" +
               (crossMidline ? "crossing" : "same_side") + "__" +
               GetVerticalZone(intendedOffsetY) + "__" +
               GetDepthZone(intendedDepth);
    }

    private Vector3 ClampCueToReachDistance(Vector3 spawnPosition, Vector3 eyePosition)
    {
        Vector3 planarOffset = spawnPosition - eyePosition;
        planarOffset.y = 0f;

        float maxReach = Mathf.Max(0.35f, MaxCueReachDistance);
        float planarDistance = planarOffset.magnitude;

        if (planarDistance > maxReach && planarDistance > 0.0001f)
        {
            planarOffset = planarOffset / planarDistance * maxReach;
            spawnPosition.x = eyePosition.x + planarOffset.x;
            spawnPosition.z = eyePosition.z + planarOffset.z;
        }

        return spawnPosition;
    }

    private static Vector3 GetHorizontalForward(Transform cameraTransform)
    {
        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        return forward.normalized;
    }

    private static Vector3 GetHorizontalRight(Transform cameraTransform)
    {
        Vector3 right = cameraTransform.right;
        right.y = 0f;

        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;

        return right.normalized;
    }
    private Vector3 ClampCueHeight(Vector3 position)
    {
        if (Camera.main == null)
            return position;

        float eyeY = Camera.main.transform.position.y;

        float minY = eyeY + CueMinHeightRelativeToEyes;
        float maxY = eyeY + CueMaxHeightRelativeToEyes;

        position.y = Mathf.Clamp(position.y, minY, maxY);

        return position;
    }
    public void SpawnTargetAfterDelay(float delay)
    {
        if (pendingSpawnCoroutine != null)
            StopCoroutine(pendingSpawnCoroutine);

        pendingSpawnCoroutine = StartCoroutine(SpawnTargetAfterDelayRoutine(delay));
    }

    private IEnumerator SpawnTargetAfterDelayRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        pendingSpawnCoroutine = null;

        if (!gameplaySessionStarted)
            yield break;

        if (TimeRemaining <= 0f || levelAdvancePending)
        {
            levelAdvancePending = false;
            NextLevel();
            yield break;
        }

        SpawnTarget();
    }
    private void FaceTargetToCamera(Transform target)
    {
        if (target == null || Camera.main == null)
            return;

        Vector3 directionToCamera = Camera.main.transform.position - target.position;
        if (directionToCamera.sqrMagnitude < 0.0001f)
            return;

        target.rotation = Quaternion.LookRotation(directionToCamera.normalized, Vector3.up);
    }

    public void CorrectHit(int points)
    {
        correctHits++;

        if (RequireLeftHand)
            leftScore += points;
        else
            rightScore += points;

        UpdateUI();
        StartCoroutine(SpawnProtection());
    }

    public void WrongHit()
    {
        if (wrongHitCooldown > 0f)
            return;

        wrongHitCooldown = wrongHitDelay;
        wrongHits++;

        if (wrongFeedbackCoroutine != null)
            StopCoroutine(wrongFeedbackCoroutine);

        wrongFeedbackCoroutine = StartCoroutine(ShowWrongHand());
    }

    private IEnumerator ShowWrongHand()
    {
        if (FeedbackText != null)
            FeedbackText.text = "Wrong Hand!";

        yield return new WaitForSeconds(1f);

        if (FeedbackText != null)
            FeedbackText.text = "";

        wrongFeedbackCoroutine = null;
    }

    private void UpdateUI()
    {
        if (LeftScoreText != null) LeftScoreText.text = leftScore.ToString();
        if (RightScoreText != null) RightScoreText.text = rightScore.ToString();

        if (PrecisionText != null)
            PrecisionText.text = "PRECISION : " + CurrentPrecision.ToString().ToUpperInvariant();

        UpdateInstructionText();

        if (LeftScoreGlow != null) LeftScoreGlow.SetActive(RequireLeftHand);
        if (RightScoreGlow != null) RightScoreGlow.SetActive(!RequireLeftHand);
    }

    private void UpdateInstructionText()
    {
        if (InstructionText == null)
            return;

        bool keepHoldPromptVisible =
            currentTarget != null &&
            CurrentCueRequiresHold;

        if (!keepHoldPromptVisible &&
            Time.time - promptShownAt > PromptVisibleDuration)
        {
            InstructionText.text = "";
            return;
        }

        string handText =
            RequireLeftHand
                ? "<color=#FF4444>LEFT</color>"
                : "<color=#44AAFF>RIGHT</color>";

        string holdText = Mathf.Approximately(CueHoldDuration, 1f) ? "1 SECOND" : "2 SECONDS";

        InstructionText.text =
            CurrentCueRequiresHold
                ? "Use " + handText + " Hand - HOLD " + holdText
                : "Use " + handText + " Hand";
    }

    private void UpdatePromptVisibility()
    {
        if (InstructionText == null)
            return;

        if (currentTarget != null && CurrentCueRequiresHold)
        {
            UpdateInstructionText();
            return;
        }

        if (Time.time - promptShownAt > PromptVisibleDuration)
            InstructionText.text = "";
    }
    public void RefreshCurrentCueInstruction()
    {
        promptShownAt = Time.time;
        UpdateInstructionText();
    }
    private void ApplyHudSpacingOnce()
    {
        if (!ApplyRuntimeHudSpacing || hudSpacingApplied)
            return;

        RectTransform upper = UpperHudRoot != null ? UpperHudRoot : LevelText != null ? LevelText.transform.parent as RectTransform : null;
        RectTransform lower = LowerInstructionRoot;

        if (lower == null && GameplayScreen != null)
        {
            Transform instructionPanel = FindChildRecursive(GameplayScreen.transform, "Instruction Panel");
            if (instructionPanel != null)
                lower = instructionPanel as RectTransform;
        }

        if (lower == null && InstructionText != null)
            lower = InstructionText.transform.parent as RectTransform;

        if (upper != null) upper.anchoredPosition += new Vector2(0f, UpperHudYOffset);
        if (lower != null && lower != upper) lower.anchoredPosition += new Vector2(0f, LowerHudYOffset);

        hudSpacingApplied = true;
    }

    private void NextLevel()
    {
        levelAdvancePending = false;

        if (pendingSpawnCoroutine != null)
        {
            StopCoroutine(pendingSpawnCoroutine);
            pendingSpawnCoroutine = null;
        }

        if (CurrentLevel >= MaxLevels)
        {
            EndGame();
            return;
        }

        CurrentLevel++;

        // Non-grid play can refresh facing by level. Assessment-grid v2 keeps one stable patient-centered origin.
        if (LockPlayAreaAtSessionStart && !IsAssessmentGridV2Active())
            LockSessionPlayArea();

        BeginSessionTimer(sessionDuration);

        SpawnTarget();
        UpdateUI();
    }

    private void EndGame()
    {
        gameplaySessionStarted = false;
        StopSessionTimer();
        FlushActiveCueSnapshot("game_end");

        if (pendingSpawnCoroutine != null)
        {
            StopCoroutine(pendingSpawnCoroutine);
            pendingSpawnCoroutine = null;
        }

        if (currentTarget != null)
        {
            Destroy(currentTarget);
            currentTarget = null;
        }

        activeTargetInteraction = null;

        if (returnToStartCoroutine != null)
        {
            StopCoroutine(returnToStartCoroutine);
            returnToStartCoroutine = null;
        }

        EnsureUiReferences();
        EnsureQuitButtons();

        if (MainMenuCanvas != null)
            MainMenuCanvas.SetActive(true);

        if (StartScreen != null)
            StartScreen.SetActive(false);

        if (WorldGameplayPanel != null)
            WorldGameplayPanel.SetActive(true);

        SetStartMenuExitVisible(false);
        SetGameplayExitCanvasVisible(false);
        SetGameplayHudRaycastsEnabled(false);

        if (ResultScreen != null)
            ResultScreen.SetActive(true);

        SetGameplayHudVisible(false);
        TryPlaceMainMenuInWorld(force: true);

        if (NeuroCueVrUiInteractor.Instance != null)
            NeuroCueVrUiInteractor.Instance.RefreshRaycasters();

        int totalScore = leftScore + rightScore;
        float accuracy = correctHits + wrongHits > 0 ? ((float)correctHits / (correctHits + wrongHits)) * 100f : 0f;

        NeuroTapPatientProfile.RecordCompletedSession(ParticipantId, totalScore);

        if (FinalLeftScoreText != null) FinalLeftScoreText.text = "LEFT SCORE : " + leftScore;
        if (FinalRightScoreText != null) FinalRightScoreText.text = "RIGHT SCORE : " + rightScore;
        if (FinalTotalScoreText != null) FinalTotalScoreText.text = "TOTAL SCORE : " + totalScore;
        if (FinalAccuracyText != null) FinalAccuracyText.text = "ACCURACY : " + Mathf.RoundToInt(accuracy) + "%";
    }

    public int GetCurrentPoints()
    {
        if (currentTarget == null)
            return 0;

        TargetInteraction target = currentTarget.GetComponent<TargetInteraction>();
        if (target == null)
            return 0;

        int remainingSeconds = Mathf.CeilToInt(target.LifeTime - target.CurrentLife);
        return Mathf.Clamp(remainingSeconds, 1, 8) * 10;
    }

    public void PlayAgain()
    {
        if (returnToStartCoroutine != null)
        {
            StopCoroutine(returnToStartCoroutine);
            returnToStartCoroutine = null;
        }

        // User chose replay — return to start menu (no automatic redirect).
        ReturnToStartMenu();
    }

    // Exact UI color from design: R0 G0 B0 A136
    private static readonly Color ExitButtonColor = new Color(0f, 0f, 0f, 136f / 255f);
    private static readonly Vector2 GameplayExitButtonSize = new Vector2(240f, 64f);

    private void EnsureQuitButtons()
    {
        // Old canvas-root Exit sat at (525,-525), off the start-menu image, so pointers never hit it.
        if (MainMenuCanvas != null)
        {
            Transform canvasExit = MainMenuCanvas.transform.Find("Exit");
            if (canvasExit != null &&
                (StartScreen == null || !canvasExit.IsChildOf(StartScreen.transform)) &&
                (WorldGameplayPanel == null || !canvasExit.IsChildOf(WorldGameplayPanel.transform)))
            {
                Destroy(canvasExit.gameObject);
            }
        }

        if (WorldGameplayPanel != null)
        {
            Transform worldExit = WorldGameplayPanel.transform.Find("Exit");
            if (worldExit != null && (ResultScreen == null || worldExit.parent != ResultScreen.transform))
                worldExit.gameObject.SetActive(false);
        }

        EnsureResultsExitButton();

        Transform startExit = FindStartMenuExit();
        if (startExit != null)
        {
            ApplyExitButtonColor(startExit.gameObject);
            Button startExitButton = startExit.GetComponent<Button>();
            if (startExitButton != null && GameplayManager.Instance != null)
            {
                startExitButton.onClick.RemoveListener(ExitGame);
                startExitButton.onClick.RemoveListener(ExitToStartMenu);
                startExitButton.onClick.AddListener(ExitGame);
            }
        }

        ApplyExitButtonColor(ExitButton);
    }

    private void EnsureResultsExitButton()
    {
        if (ResultScreen == null)
            return;

        Transform startExit = FindStartMenuExit();
        Vector2 size = GameplayExitButtonSize;
        if (startExit != null)
            size = startExit.GetComponent<RectTransform>().sizeDelta;

        Transform existing = ResultScreen.transform.Find("Exit");
        GameObject exitObject = existing != null
            ? existing.gameObject
            : CreateUiPanel(ResultScreen.transform, "Exit", new Vector2(0f, -340f), size, ExitButtonColor);

        RectTransform exitRect = exitObject.GetComponent<RectTransform>();
        if (exitRect != null)
            ApplyCenteredRect(exitRect, new Vector2(0f, -340f), size);

        Image exitImage = exitObject.GetComponent<Image>();
        if (exitImage == null)
            exitImage = exitObject.AddComponent<Image>();
        exitImage.color = ExitButtonColor;
        exitImage.raycastTarget = true;

        Button button = exitObject.GetComponent<Button>();
        if (button == null)
            button = exitObject.AddComponent<Button>();
        button.targetGraphic = exitImage;
        button.interactable = true;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(ExitGame);

        EnsureExitLabel(exitObject);
        exitObject.SetActive(true);
    }

    private Transform FindStartMenuExit()
    {
        if (StartScreen == null)
            return null;

        Transform panel = StartScreen.transform.Find("Menu Panel");
        if (panel != null)
        {
            Transform exit = panel.Find("Exit");
            if (exit != null)
                return exit;
        }

        return StartScreen.transform.Find("Exit");
    }

    private static void ApplyExitButtonColor(GameObject exitObject)
    {
        if (exitObject == null)
            return;

        Image image = exitObject.GetComponent<Image>();
        if (image != null)
            image.color = ExitButtonColor;
    }

    private void SetStartMenuExitVisible(bool visible)
    {
        Transform startExit = FindStartMenuExit();
        if (startExit != null)
            startExit.gameObject.SetActive(visible);
    }

    public void LogCueResult(CueResult result)
    {
        if (!EnableDataLogging)
            return;

        InitializeDataLogging();

        result.SessionId = string.IsNullOrEmpty(result.SessionId) ? sessionId : result.SessionId;

        string[] values =
        {
            result.ParticipantId, I(result.SessionNumber), result.DaysSinceStrokeOrInjury, result.AffectedBodySide, result.LesionSide,
            result.SuspectedNeglectSide, result.DominantHand, result.AssessmentOrTrainingMode, result.AssessmentProtocolVersion,
            result.AppVersion, result.HeadsetModel, I(result.IsPracticeTrial), I(result.AssessmentRepetitionNumber),
            I(result.AssessmentGridRow), I(result.AssessmentGridColumn), I(result.RandomizationOrder),
            result.SessionId, result.EventType, I(result.CueSequence), I(result.CueNumber), I(result.Level), result.CueConditionId,
            I(result.ArmSide), result.RequiredHandLabel, I(result.CueHemispace), result.CueHemispaceLabel, I(result.CrossMidline),
            result.HorizontalZone, result.VerticalZone, result.DepthZone, F(result.IntendedOffsetXM), F(result.IntendedOffsetYM),
            F(result.IntendedDepthM), F(result.LevelCrossingProbability), F(result.LevelSideOffsetM),
            I(result.Tapped), I(result.RequiresHold), result.CompletionReason, result.CueStartUtc, result.CueEndUtc,
            F(result.CueStartGameTime), F(result.CueEndGameTime), F(result.AnyArmReactionTime), F(result.CorrectArmReactionTime),
            F(result.MovementStartTime), F(result.EngagementTime), F(result.SecondsHeld), I(result.WrongAttempts),
            F(result.CueWorldPosition.x), F(result.CueWorldPosition.y), F(result.CueWorldPosition.z),
            F(result.CueViewportPosition.x), F(result.CueViewportPosition.y), F(result.CueScreenPosition.x), F(result.CueScreenPosition.y),
            F(result.OptimalIndexWorldPosition.x), F(result.OptimalIndexWorldPosition.y), F(result.OptimalIndexWorldPosition.z),
            result.FirstAnyArmSide,
            F(result.FirstAnyTouchWorld.x), F(result.FirstAnyTouchWorld.y), F(result.FirstAnyTouchWorld.z),
            F(result.FirstCorrectTouchWorld.x), F(result.FirstCorrectTouchWorld.y), F(result.FirstCorrectTouchWorld.z),
            F(result.CompletionTouchWorld.x), F(result.CompletionTouchWorld.y), F(result.CompletionTouchWorld.z),
            result.ContactRegion, F(result.TargetRadius), F(result.DistanceFromCenterOnTouch), F(result.HoldAverageDrift),
            F(result.HoldStabilityH), F(result.ActualPathLength), F(result.IdealPathLength), F(result.PathEfficiencyE),
            F(result.AccuracyA), F(result.ReactionScoreR), F(result.CoordinationScore), F(result.PeakHandSpeed),
            F(result.AverageHandSpeed), F(result.MovementDuration), F(result.TimeToPeakSpeed), I(result.NumberOfDirectionChanges),
            F(result.PathSmoothness), F(result.EndpointErrorX), F(result.EndpointErrorY), F(result.OvershootDistance),
            F(result.UndershootDistance), I(result.HandTrackingLostCount), F(result.HandTrackingLostDuration),
            F(result.HeadRotationYAtCueStart), F(result.HeadRotationYAtTouch), I(result.HeadMovedDuringCue),
            I(result.CueVisibleInViewportAtSpawn),
            F(result.MovementOnsetTimeS), F(result.MovementOnsetLatencyS), F(result.MovementOnsetSpeedThresholdMps),
            F(result.InitiationLatencyS), F(result.TransportTimeS), F(result.StabilizationTimeS),
            F(result.MovementStartPosition.x), F(result.MovementStartPosition.y), F(result.MovementStartPosition.z),
            F(result.MovementStartWorldPosition.x), F(result.MovementStartWorldPosition.y), F(result.MovementStartWorldPosition.z),
            F(result.ReachAmplitudeM), F(result.MeanSpeedMps), F(result.PeakSpeedMps), F(result.MeanSpeedNormalized),
            F(result.PeakSpeedNormalized), F(result.NormalizedJerk), I(result.CorrectiveSubmovementCount),
            F(result.PrimaryVelocityPeakMps), I(result.SecondaryVelocityPeakCount), F(result.InitialDirectionErrorDeg),
            F(result.TrajectoryCorridorRadiusM), F(result.TimeOutsideTrajectoryCorridorS), F(result.PercentTransportOutsideCorridor),
            F(result.MaxTrajectoryDeviationM), F(result.MeanTrajectoryDeviationM), F(result.HeadStartPosition.x),
            F(result.HeadStartPosition.y), F(result.HeadStartPosition.z), F(result.HeadContactPosition.x),
            F(result.HeadContactPosition.y), F(result.HeadContactPosition.z), F(result.HeadTranslation.x),
            F(result.HeadTranslation.y), F(result.HeadTranslation.z), F(result.HeadTranslationTotalM),
            F(result.HeadStartRotation.y), F(result.HeadStartRotation.x), F(result.HeadStartRotation.z),
            F(result.HeadContactRotation.y), F(result.HeadContactRotation.x), F(result.HeadContactRotation.z),
            F(result.HeadDeltaRotation.y), F(result.HeadDeltaRotation.x), F(result.HeadDeltaRotation.z),
            I(result.ContralateralMovementDetected), F(result.ContralateralMovementOnsetTimeS),
            F(result.ContralateralMovementOnsetLatencyS), F(result.ContralateralMaxDisplacementM),
            F(result.ContralateralPathLengthM), F(result.ContralateralPeakSpeedMps), F(result.ContralateralTargetApproachM),
            I(result.CueSequenceNumber), F(result.ElapsedSessionTimeS), F(result.NormalizedSessionPosition),
            F(result.PlannedSessionDurationS), F(result.ActualSessionDurationS), F(result.IntercueIntervalS),
            F(result.TargetRadiusM), F(result.TargetDiameterM), F(result.TargetPosition.x), F(result.TargetPosition.y),
            F(result.TargetPosition.z), result.Hemispace, result.RequiredHand, result.PrecisionMode, F(result.HoldRequiredS),
            F(result.CueTimeoutS), I(result.SmartCueEnabled), result.SmartCueZone, result.SmartCueZoneClassification,
            result.CueSpawnMethod, I(result.TrajectoryExpectedSampleCount), I(result.TrajectoryValidSampleCount),
            F(result.TrajectoryValidSamplePercent), F(result.RequiredHandValidSamplePercent),
            F(result.ContralateralHandValidSamplePercent), F(result.HeadValidSamplePercent),
            result.KinematicProcessingVersion, result.DataSchemaVersion
        };

        try
        {
            File.AppendAllText(dataFilePath, ToCsvLine(values) + Environment.NewLine);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not write NeuroCue data: " + exception.Message);
        }
    }

    public void ClearActiveTarget(TargetInteraction target)
    {
        if (activeTargetInteraction == target)
            activeTargetInteraction = null;
    }

    private void InitializeDataLogging()
    {
        if (!EnableDataLogging || !string.IsNullOrEmpty(dataFilePath))
            return;

        try
        {
            string folder = Path.Combine(Application.persistentDataPath, DataFolderName);
            Directory.CreateDirectory(folder);

            dataFilePath = Path.Combine(folder, "NeuroCue_" + sessionId + ".csv");

            if (!File.Exists(dataFilePath))
                File.WriteAllText(dataFilePath, CsvHeader + Environment.NewLine);

            Debug.Log("NeuroCue data file: " + dataFilePath);
        }
        catch (Exception exception)
        {
            EnableDataLogging = false;
            Debug.LogWarning("NeuroCue data logging disabled: " + exception.Message);
        }
    }

    private void FlushActiveCueSnapshot(string reason)
    {
        if (activeTargetInteraction != null)
            activeTargetInteraction.ForceLogSnapshot(reason);
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause)
            FlushActiveCueSnapshot("app_paused");
    }

    private void OnApplicationQuit()
    {
        FlushActiveCueSnapshot("app_quit");
    }

    private static string ToCsvLine(string[] values)
    {
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(EscapeCsv(values[i]));
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    private static string F(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return "";

        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string I(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
