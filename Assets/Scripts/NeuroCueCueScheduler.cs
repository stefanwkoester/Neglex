using System;
using System.Collections.Generic;
using UnityEngine;

public enum NeuroCueSide
{
    Left = 0,
    Right = 1
}

public enum NeuroCueScheduleMode
{
    RandomWeighted,
    Alternating,
    SameSideBlocks,
    AffectedSideBias,
    Assessment,
    CustomProtocol,
    LevelBasedAssessment
}

public enum NeuroCueLocationScheduleMode
{
    CrossMidlineByLevel,
    RandomWeighted
}

[Serializable]
public class NeuroCueSideScheduleSettings
{
    public NeuroCueScheduleMode Mode = NeuroCueScheduleMode.LevelBasedAssessment;

    [Header("Level-Based Assessment")]
    [Tooltip("Level 3 alternates most of the time, but intentionally repeats the same hand sometimes.")]
    [Range(0f, 1f)] public float Level3AlternateProbability = 0.8f;
    [Tooltip("Level 4 allows this many same-hand repeats after the first cue. 1 means max run is 2, such as L,L.")]
    public int Level4MaxRepeatsAfterFirst = 1;
    [Tooltip("Level 5 allows this many same-hand repeats after the first cue. 2 means max run is 3, such as L,L,L.")]
    public int Level5MaxRepeatsAfterFirst = 2;

    [Header("Random Weighted")]
    [Range(0f, 1f)] public float LeftProbability = 0.5f;
    [Tooltip("When enabled, gently nudges random choices back toward the desired ratio over time without using batches.")]
    public bool CorrectLongRunDrift = true;
    public float DriftCorrectionStrength = 0.35f;

    [Header("Affected Side Bias")]
    public NeuroCueSide AffectedSide = NeuroCueSide.Left;
    [Range(0f, 1f)] public float AffectedSideProbability = 0.7f;

    [Header("Repeat Control")]
    [Tooltip("0 means no repeat limit. Use 2 or 3 later if you want to prevent long same-side streaks.")]
    public int MaxSameSideRepeats = 0;
    public int SameSideBlockLength = 4;

    [Header("Assessment")]
    public List<NeuroCueSide> AssessmentSequence = new List<NeuroCueSide>
    {
        NeuroCueSide.Left,
        NeuroCueSide.Right,
        NeuroCueSide.Left,
        NeuroCueSide.Right
    };

    [Header("Custom Protocol")]
    public List<NeuroCueSide> CustomSequence = new List<NeuroCueSide>();
}

public class NeuroCueSideScheduler
{
    private NeuroCueSideScheduleSettings settings;
    private NeuroCueSide lastSide = NeuroCueSide.Left;
    private NeuroCueSide currentBlockSide = NeuroCueSide.Left;
    private int sameSideRepeatCount;
    private int blockRemaining;
    private int assessmentIndex;
    private int customIndex;
    private int leftCount;
    private int rightCount;
    private bool hasLastSide;
    private bool alternateNextLeft = true;

    public void Reset(NeuroCueSideScheduleSettings newSettings)
    {
        settings = newSettings ?? new NeuroCueSideScheduleSettings();
        hasLastSide = false;
        sameSideRepeatCount = 0;
        blockRemaining = 0;
        assessmentIndex = 0;
        customIndex = 0;
        leftCount = 0;
        rightCount = 0;
        alternateNextLeft = true;
        currentBlockSide = settings.AffectedSide;
    }

    public NeuroCueSide Next()
    {
        return Next(1);
    }

    public NeuroCueSide Next(int level)
    {
        if (settings == null)
            Reset(null);

        NeuroCueSide proposed;
        int maxConsecutiveSameHandOverride = 0;

        switch (settings.Mode)
        {
            case NeuroCueScheduleMode.LevelBasedAssessment:
                proposed = NextLevelBasedAssessment(level, out maxConsecutiveSameHandOverride);
                break;
            case NeuroCueScheduleMode.RandomWeighted:
                proposed = NextRandomWeighted();
                break;
            case NeuroCueScheduleMode.Alternating:
                proposed = NextAlternating();
                break;
            case NeuroCueScheduleMode.SameSideBlocks:
                proposed = NextBlockSide();
                break;
            case NeuroCueScheduleMode.AffectedSideBias:
                proposed = NextWeighted();
                break;
            case NeuroCueScheduleMode.Assessment:
                proposed = NextFromSequence(settings.AssessmentSequence, ref assessmentIndex);
                break;
            case NeuroCueScheduleMode.CustomProtocol:
                proposed = NextFromSequence(settings.CustomSequence, ref customIndex);
                break;
            default:
                proposed = NextRandomWeighted();
                break;
        }

        proposed = ApplyRepeatLimit(proposed, maxConsecutiveSameHandOverride);
        Remember(proposed);
        return proposed;
    }

    private NeuroCueSide NextLevelBasedAssessment(int level, out int maxConsecutiveSameHandOverride)
    {
        maxConsecutiveSameHandOverride = 0;

        switch (Mathf.Clamp(level, 1, 5))
        {
            case 1:
            case 2:
                return NextAlternating();
            case 3:
                maxConsecutiveSameHandOverride = 2;
                return NextMostlyAlternating(settings.Level3AlternateProbability);
            case 4:
                maxConsecutiveSameHandOverride = RepeatsAfterFirstToMaxRun(settings.Level4MaxRepeatsAfterFirst);
                return NextRandomWeighted();
            default:
                maxConsecutiveSameHandOverride = RepeatsAfterFirstToMaxRun(settings.Level5MaxRepeatsAfterFirst);
                return NextRandomWeighted();
        }
    }

    private NeuroCueSide NextRandomWeighted()
    {
        float leftProbability = Mathf.Clamp01(settings.LeftProbability);

        if (settings.CorrectLongRunDrift)
        {
            int total = leftCount + rightCount;
            if (total > 0)
            {
                float observedLeftRate = (float)leftCount / total;
                float drift = leftProbability - observedLeftRate;
                leftProbability = Mathf.Clamp01(leftProbability + drift * Mathf.Max(0f, settings.DriftCorrectionStrength));
            }
        }

        return UnityEngine.Random.value < leftProbability ? NeuroCueSide.Left : NeuroCueSide.Right;
    }

    private NeuroCueSide NextAlternating()
    {
        NeuroCueSide side = alternateNextLeft ? NeuroCueSide.Left : NeuroCueSide.Right;
        alternateNextLeft = !alternateNextLeft;
        return side;
    }

    private NeuroCueSide NextMostlyAlternating(float alternateProbability)
    {
        if (!hasLastSide)
            return NextAlternating();

        bool alternate = UnityEngine.Random.value < Mathf.Clamp01(alternateProbability);
        return alternate ? Opposite(lastSide) : lastSide;
    }

    private NeuroCueSide NextBlockSide()
    {
        if (blockRemaining <= 0)
        {
            currentBlockSide = currentBlockSide == NeuroCueSide.Left ? NeuroCueSide.Right : NeuroCueSide.Left;
            blockRemaining = Mathf.Max(1, settings.SameSideBlockLength);
        }

        blockRemaining--;
        return currentBlockSide;
    }

    private NeuroCueSide NextWeighted()
    {
        float affectedProbability = Mathf.Clamp01(settings.AffectedSideProbability);
        bool chooseAffected = UnityEngine.Random.value < affectedProbability;

        if (chooseAffected)
            return settings.AffectedSide;

        return settings.AffectedSide == NeuroCueSide.Left ? NeuroCueSide.Right : NeuroCueSide.Left;
    }

    private NeuroCueSide NextFromSequence(List<NeuroCueSide> sequence, ref int index)
    {
        if (sequence == null || sequence.Count == 0)
            return NextRandomWeighted();

        NeuroCueSide side = sequence[index % sequence.Count];
        index++;
        return side;
    }

    private NeuroCueSide ApplyRepeatLimit(NeuroCueSide proposed, int maxConsecutiveSameHandOverride = 0)
    {
        int maxRepeats = maxConsecutiveSameHandOverride > 0 ? maxConsecutiveSameHandOverride : settings.MaxSameSideRepeats;

        if (maxRepeats <= 0)
            return proposed;

        if (!hasLastSide || proposed != lastSide || sameSideRepeatCount < maxRepeats)
            return proposed;

        return proposed == NeuroCueSide.Left ? NeuroCueSide.Right : NeuroCueSide.Left;
    }

    private static int RepeatsAfterFirstToMaxRun(int repeatsAfterFirst)
    {
        return Mathf.Max(1, repeatsAfterFirst) + 1;
    }

    private static NeuroCueSide Opposite(NeuroCueSide side)
    {
        return side == NeuroCueSide.Left ? NeuroCueSide.Right : NeuroCueSide.Left;
    }

    private void Remember(NeuroCueSide side)
    {
        if (side == NeuroCueSide.Left)
            leftCount++;
        else
            rightCount++;

        if (hasLastSide && side == lastSide)
            sameSideRepeatCount++;
        else
            sameSideRepeatCount = 1;

        lastSide = side;
        hasLastSide = true;
    }
}

[Serializable]
public class NeuroCueLocationScheduleSettings
{
    public NeuroCueLocationScheduleMode Mode = NeuroCueLocationScheduleMode.CrossMidlineByLevel;

    [Header("Cross-Midline Probability By Level")]
    [Range(0f, 1f)] public float Level1CrossingProbability = 0.15f;
    [Range(0f, 1f)] public float Level2CrossingProbability = 0.25f;
    [Range(0f, 1f)] public float Level3CrossingProbability = 0.35f;
    [Range(0f, 1f)] public float Level4CrossingProbability = 0.45f;
    [Range(0f, 1f)] public float Level5CrossingProbability = 0.55f;

    [Header("Hemispace Offset By Level")]
    public float Level1SideOffsetMeters = 0.10f;
    public float Level2SideOffsetMeters = 0.15f;
    public float Level3SideOffsetMeters = 0.20f;
    public float Level4SideOffsetMeters = 0.25f;
    public float Level5SideOffsetMeters = 0.30f;

    [Header("Optional Drift Control")]
    [Tooltip("When enabled, gently nudges crossing frequency toward the level target over time without using batches.")]
    public bool CorrectLongRunDrift = true;
    public float DriftCorrectionStrength = 0.35f;

    [Header("Repeat Control")]
    [Tooltip("0 means no repeat limit. Use 2 or 3 later to avoid long crossing or same-side streaks.")]
    public int MaxSameConditionRepeats = 0;
}

public struct NeuroCueLocationResult
{
    public NeuroCueSide Hemispace;
    public bool CrossesMidline;
    public float SideOffsetMeters;
}

public class NeuroCueLocationScheduler
{
    private NeuroCueLocationScheduleSettings settings;
    private bool hasLastCrossing;
    private bool lastCrossing;
    private int sameConditionRepeatCount;
    private int crossingCount;
    private int sameSideCount;

    public void Reset(NeuroCueLocationScheduleSettings newSettings)
    {
        settings = newSettings ?? new NeuroCueLocationScheduleSettings();
        hasLastCrossing = false;
        lastCrossing = false;
        sameConditionRepeatCount = 0;
        crossingCount = 0;
        sameSideCount = 0;
    }

    public NeuroCueLocationResult Next(NeuroCueSide requiredHand, int level)
    {
        if (settings == null)
            Reset(null);

        bool crossesMidline = NextCrossingDecision(level);
        crossesMidline = ApplyRepeatLimit(crossesMidline);
        Remember(crossesMidline);

        NeuroCueSide hemispace = crossesMidline ? Opposite(requiredHand) : requiredHand;

        return new NeuroCueLocationResult
        {
            Hemispace = hemispace,
            CrossesMidline = crossesMidline,
            SideOffsetMeters = GetSideOffsetMeters(level)
        };
    }

    private bool NextCrossingDecision(int level)
    {
        float crossingProbability = GetCrossingProbability(level);

        if (settings.CorrectLongRunDrift)
        {
            int total = crossingCount + sameSideCount;
            if (total > 0)
            {
                float observedCrossingRate = (float)crossingCount / total;
                float drift = crossingProbability - observedCrossingRate;
                crossingProbability = Mathf.Clamp01(crossingProbability + drift * Mathf.Max(0f, settings.DriftCorrectionStrength));
            }
        }

        return UnityEngine.Random.value < crossingProbability;
    }

    private float GetCrossingProbability(int level)
    {
        return GetCrossingProbabilityForLevel(settings, level);
    }

    public static float GetCrossingProbabilityForLevel(NeuroCueLocationScheduleSettings sourceSettings, int level)
    {
        NeuroCueLocationScheduleSettings activeSettings = sourceSettings ?? new NeuroCueLocationScheduleSettings();

        switch (Mathf.Clamp(level, 1, 5))
        {
            case 1:
                return Mathf.Clamp01(activeSettings.Level1CrossingProbability);
            case 2:
                return Mathf.Clamp01(activeSettings.Level2CrossingProbability);
            case 3:
                return Mathf.Clamp01(activeSettings.Level3CrossingProbability);
            case 4:
                return Mathf.Clamp01(activeSettings.Level4CrossingProbability);
            default:
                return Mathf.Clamp01(activeSettings.Level5CrossingProbability);
        }
    }

    private float GetSideOffsetMeters(int level)
    {
        switch (Mathf.Clamp(level, 1, 5))
        {
            case 1:
                return Mathf.Max(0f, settings.Level1SideOffsetMeters);
            case 2:
                return Mathf.Max(0f, settings.Level2SideOffsetMeters);
            case 3:
                return Mathf.Max(0f, settings.Level3SideOffsetMeters);
            case 4:
                return Mathf.Max(0f, settings.Level4SideOffsetMeters);
            default:
                return Mathf.Max(0f, settings.Level5SideOffsetMeters);
        }
    }

    private bool ApplyRepeatLimit(bool proposedCrossing)
    {
        int maxRepeats = settings.MaxSameConditionRepeats;

        if (maxRepeats <= 0)
            return proposedCrossing;

        if (!hasLastCrossing || proposedCrossing != lastCrossing || sameConditionRepeatCount < maxRepeats)
            return proposedCrossing;

        return !proposedCrossing;
    }

    private void Remember(bool crossesMidline)
    {
        if (crossesMidline)
            crossingCount++;
        else
            sameSideCount++;

        if (hasLastCrossing && crossesMidline == lastCrossing)
            sameConditionRepeatCount++;
        else
            sameConditionRepeatCount = 1;

        lastCrossing = crossesMidline;
        hasLastCrossing = true;
    }

    private static NeuroCueSide Opposite(NeuroCueSide side)
    {
        return side == NeuroCueSide.Left ? NeuroCueSide.Right : NeuroCueSide.Left;
    }
}
