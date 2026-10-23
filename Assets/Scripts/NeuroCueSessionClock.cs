using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Independent session countdown. Runs in its own Update so GameplayManager state
/// flags cannot freeze the HUD at 01:00 while cues/scores keep working.
/// </summary>
[DefaultExecutionOrder(1000)]
public class NeuroCueSessionClock : MonoBehaviour
{
    public static NeuroCueSessionClock Instance { get; private set; }

    private float durationSeconds = 60f;
    private float endsAtUnscaled;
    private bool running;
    private bool expireNotified;

    private TMP_Text timerText;
    private TMP_Text timerText2;
    private TMP_Text levelText;
    private Image fillImage;
    private GameObject hudRoot;
    private int levelNumber = 1;

    private System.Action onExpired;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void BindUi(GameObject gameplayHud, TMP_Text primary, TMP_Text secondary, TMP_Text level, Image fill)
    {
        hudRoot = gameplayHud;
        timerText = primary;
        timerText2 = secondary;
        levelText = level;
        fillImage = fill;
        ResolveMissingUi();
    }

    public void SetLevel(int level)
    {
        levelNumber = Mathf.Max(1, level);
        RefreshUi();
    }

    public void StartCountdown(float seconds, System.Action expiredCallback)
    {
        durationSeconds = Mathf.Max(1f, seconds);
        endsAtUnscaled = Time.unscaledTime + durationSeconds;
        onExpired = expiredCallback;
        expireNotified = false;
        running = true;
        RefreshUi();
    }

    public void StopCountdown()
    {
        running = false;
        expireNotified = false;
        onExpired = null;
    }

    public bool IsRunning => running;

    public float TimeRemaining => running
        ? Mathf.Max(0f, endsAtUnscaled - Time.unscaledTime)
        : 0f;

    private void Update()
    {
        if (!running)
            return;

        // If HUD is up, keep counting even if something else got confused.
        RefreshUi();

        if (TimeRemaining > 0f)
            return;

        if (expireNotified)
            return;

        expireNotified = true;
        running = false;
        onExpired?.Invoke();
    }

    private void RefreshUi()
    {
        ResolveMissingUi();

        float remaining = running ? Mathf.Max(0f, endsAtUnscaled - Time.unscaledTime) : 0f;
        int seconds = remaining <= 0f ? 0 : Mathf.CeilToInt(remaining);
        string clock = $"{seconds / 60:00}:{seconds % 60:00}";

        if (timerText != null) timerText.text = clock;
        if (timerText2 != null) timerText2.text = clock;
        if (levelText != null) levelText.text = "LEVEL " + levelNumber;
        if (fillImage != null)
            fillImage.fillAmount = Mathf.Clamp01(remaining / Mathf.Max(0.01f, durationSeconds));

        if (hudRoot == null)
            return;

        TMP_Text[] texts = hudRoot.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null)
                continue;

            string n = text.gameObject.name;
            if (n == "Timer Text" || n == "Timer")
                text.text = clock;
            else if (n == "Level Text" || n == "Level")
                text.text = "LEVEL " + levelNumber;
        }
    }

    private void ResolveMissingUi()
    {
        if (hudRoot == null)
        {
            GameObject found = GameObject.Find("Gameplay");
            if (found != null)
                hudRoot = found;
        }

        if (hudRoot == null)
            return;

        if (timerText == null)
        {
            Transform t = FindChild(hudRoot.transform, "Timer Text");
            if (t != null) timerText = t.GetComponent<TMP_Text>();
        }

        if (timerText2 == null)
        {
            Transform t = FindChild(hudRoot.transform, "Timer");
            if (t != null) timerText2 = t.GetComponent<TMP_Text>();
        }

        if (levelText == null)
        {
            Transform t = FindChild(hudRoot.transform, "Level Text");
            if (t == null) t = FindChild(hudRoot.transform, "Level");
            if (t != null) levelText = t.GetComponent<TMP_Text>();
        }

        if (fillImage == null)
        {
            Image[] images = hudRoot.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].type == Image.Type.Filled)
                {
                    fillImage = images[i];
                    break;
                }
            }
        }
    }

    private static Transform FindChild(Transform parent, string name)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name)
                return child;

            Transform nested = FindChild(child, name);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
