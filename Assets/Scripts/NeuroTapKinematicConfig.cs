public static class NeuroTapKinematicConfig
{
    public const float MovementSpeedThresholdMps = 0.05f;
    public const float MovementSustainDurationSec = 0.100f;
    public const float StartPositionWindowSec = 0.100f;
    public const float InitialDirectionWindowSec = 0.100f;

    public const float SubmovementMinPeakRatio = 0.15f;
    public const float SubmovementMinSeparationSec = 0.120f;
    public const float SubmovementMinValleyDropRatio = 0.20f;

    public const float TrajectoryCorridorMinRadiusM = 0.03f;
    public const float TrajectoryCorridorTargetRadiusMultiplier = 1.5f;
    public const float TargetTrajectorySampleRateHz = 60f;

    public const string KinematicProcessingVersion = "1.0";
    public const string DataSchemaVersion = "1.0";
    public const string DefaultCueSpawnMethod = "RANDOM";
    public const string DefaultSmartCueZoneClassification = "UNCLASSIFIED";
}
