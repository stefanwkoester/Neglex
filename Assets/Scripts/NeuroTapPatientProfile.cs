using System;
using UnityEngine;

public enum NeuroTapSideToPractice
{
    Left = 0,
    Right = 1,
    Both = 2,
    NotSure = 3
}

public enum NeuroTapDifficultyPreset
{
    Easy = 0,
    Medium = 1,
    Hard = 2
}

public static class NeuroTapPatientProfile
{
    private const string InstallNumberKey = "NeuroTap.InstallNumber";
    private const string UsernameKey = "NeuroTap.Username";
    private const string SideToPracticeKey = "NeuroTap.SideToPractice";
    private const string SessionMinutesKey = "NeuroTap.SessionMinutes";
    private const string RemindersEnabledKey = "NeuroTap.RemindersEnabled";
    private const string DifficultyPresetKey = "NeuroTap.DifficultyPreset";
    private const string ActiveDataUsernameKey = "NeuroTap.ActiveDataUsername";

    public static string InstallationNumber
    {
        get
        {
            string value = PlayerPrefs.GetString(InstallNumberKey, "");
            if (string.IsNullOrWhiteSpace(value))
            {
                value = "001";
                PlayerPrefs.SetString(InstallNumberKey, value);
                PlayerPrefs.Save();
            }

            return NormalizeInstallNumber(value);
        }
        set
        {
            PlayerPrefs.SetString(InstallNumberKey, NormalizeInstallNumber(value));
            PlayerPrefs.Save();
        }
    }

    public static string Username
    {
        get
        {
            string value = PlayerPrefs.GetString(UsernameKey, "");
            if (string.IsNullOrWhiteSpace(value))
            {
                value = "user" + InstallationNumber;
                PlayerPrefs.SetString(UsernameKey, value);
                PlayerPrefs.Save();
            }

            return value.Trim();
        }
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "user" + InstallationNumber : value.Trim();
            PlayerPrefs.SetString(UsernameKey, normalized);
            PlayerPrefs.Save();
        }
    }

    public static string GuestUsername => "guestuser" + InstallationNumber;

    public static NeuroTapSideToPractice SideToPractice
    {
        get => (NeuroTapSideToPractice)Mathf.Clamp(PlayerPrefs.GetInt(SideToPracticeKey, (int)NeuroTapSideToPractice.NotSure), 0, 3);
        set
        {
            PlayerPrefs.SetInt(SideToPracticeKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    public static int SessionMinutes
    {
        get
        {
            int minutes = PlayerPrefs.GetInt(SessionMinutesKey, 3);
            return minutes == 3 || minutes == 5 || minutes == 7 ? minutes : 3;
        }
        set
        {
            int minutes = value == 3 || value == 5 || value == 7 ? value : 3;
            PlayerPrefs.SetInt(SessionMinutesKey, minutes);
            PlayerPrefs.Save();
        }
    }

    public static bool RemindersEnabled
    {
        get => PlayerPrefs.GetInt(RemindersEnabledKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(RemindersEnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static NeuroTapDifficultyPreset DifficultyPreset
    {
        get => (NeuroTapDifficultyPreset)Mathf.Clamp(PlayerPrefs.GetInt(DifficultyPresetKey, (int)NeuroTapDifficultyPreset.Medium), 0, 2);
        set
        {
            PlayerPrefs.SetInt(DifficultyPresetKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    public static string ActiveDataUsername => PlayerPrefs.GetString(ActiveDataUsernameKey, Username);
    public static int LastScore => GetLastScore(Username);
    public static int BestScore => GetBestScore(Username);
    public static int SessionsCompleted => GetSessionsCompleted(Username);

    public static int GetLastScore(string username)
    {
        return PlayerPrefs.GetInt(StatsKey(username, "LastScore"), 0);
    }

    public static int GetBestScore(string username)
    {
        return PlayerPrefs.GetInt(StatsKey(username, "BestScore"), 0);
    }

    public static int GetSessionsCompleted(string username)
    {
        return PlayerPrefs.GetInt(StatsKey(username, "SessionsCompleted"), 0);
    }

    public static void ApplyToGameplay(GameplayManager gameplayManager, bool guestSession)
    {
        if (gameplayManager == null)
            return;

        string activeUsername = guestSession ? GuestUsername : Username;
        PlayerPrefs.SetString(ActiveDataUsernameKey, activeUsername);
        PlayerPrefs.Save();

        gameplayManager.ParticipantId = activeUsername;
        gameplayManager.SessionNumber = GetSessionsCompleted(activeUsername) + 1;
        gameplayManager.AssessmentRepetitionNumber = 1;
        gameplayManager.AssessmentGridRow = 0;
        gameplayManager.AssessmentGridColumn = 0;
        gameplayManager.AffectedBodySide = ToStoredSideLabel(SideToPractice);
        gameplayManager.SessionDuration = SessionMinutes * 60f / 5f;
        gameplayManager.CueHoldDuration = 1f;
        gameplayManager.UseTwoSecondHold = false;
        gameplayManager.AssessmentOrTrainingMode = "assessment";
        gameplayManager.AssessmentProtocolVersion = "assessment_grid_v2";
        gameplayManager.CurrentPrecision = ToPrecisionMode(DifficultyPreset);
    }

    private static GameplayManager.PrecisionMode ToPrecisionMode(NeuroTapDifficultyPreset difficulty)
    {
        switch (difficulty)
        {
            case NeuroTapDifficultyPreset.Easy:
                return GameplayManager.PrecisionMode.Low;
            case NeuroTapDifficultyPreset.Hard:
                return GameplayManager.PrecisionMode.High;
            default:
                return GameplayManager.PrecisionMode.Medium;
        }
    }

    public static void RecordCompletedSession(int totalScore)
    {
        RecordCompletedSession(ActiveDataUsername, totalScore);
    }

    public static void RecordCompletedSession(string username, int totalScore)
    {
        string activeUsername = string.IsNullOrWhiteSpace(username) ? Username : username.Trim();
        int score = Mathf.Max(0, totalScore);

        PlayerPrefs.SetInt(StatsKey(activeUsername, "LastScore"), score);
        PlayerPrefs.SetInt(StatsKey(activeUsername, "BestScore"), Mathf.Max(GetBestScore(activeUsername), score));
        PlayerPrefs.SetInt(StatsKey(activeUsername, "SessionsCompleted"), GetSessionsCompleted(activeUsername) + 1);
        PlayerPrefs.Save();
    }

    public static string ToDisplaySideLabel(NeuroTapSideToPractice side)
    {
        switch (side)
        {
            case NeuroTapSideToPractice.Left:
                return "Left";
            case NeuroTapSideToPractice.Right:
                return "Right";
            case NeuroTapSideToPractice.Both:
                return "Both";
            default:
                return "Not sure";
        }
    }

    private static string ToStoredSideLabel(NeuroTapSideToPractice side)
    {
        switch (side)
        {
            case NeuroTapSideToPractice.Left:
                return "left";
            case NeuroTapSideToPractice.Right:
                return "right";
            case NeuroTapSideToPractice.Both:
                return "both";
            default:
                return "not_sure";
        }
    }

    private static string NormalizeInstallNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "001";

        string digits = "";
        for (int i = 0; i < raw.Length; i++)
        {
            if (char.IsDigit(raw[i]))
                digits += raw[i];
        }

        if (int.TryParse(digits, out int number))
            return Mathf.Clamp(number, 1, 999).ToString("000");

        return "001";
    }

    private static string StatsKey(string username, string field)
    {
        return "NeuroTap.Stats." + SanitizeKeyPart(username) + "." + field;
    }

    private static string SanitizeKeyPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        string output = "";
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                output += c;
        }

        return string.IsNullOrEmpty(output) ? "unknown" : output;
    }
}
