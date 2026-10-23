using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(1000)]
public class NeuroTapStartMenuController : MonoBehaviour
{
    [Header("References")]
    public GameplayManager GameplayManager;
    public GameObject MainMenuCanvas;
    public GameObject StartScreen;
    public Sprite LogoSprite;

    [Header("Layout")]
    public bool BuildMenuOnStart = true;
    public bool ReplaceExistingStartScreenChildren = true;
    public Vector2 PanelSize = new Vector2(660f, 540f);
    public Color PanelColor = new Color(0.02f, 0.12f, 0.18f, 0.78f);
    public Color PrimaryBlue = new Color(0.0f, 0.55f, 0.95f, 0.94f);
    public Color SecondaryBlue = new Color(0.06f, 0.24f, 0.34f, 0.88f);

    private TMP_Text statsPreviewText;
    private TMP_Text resultsText;
    private GameObject resultsPanel;
    private GameObject settingsPanel;
    private TMP_InputField usernameInput;
    private TMP_Text sideValueText;
    private TMP_Text sessionDurationValueText;
    private TMP_Text difficultyValueText;
    private Toggle remindersToggle;
    private TouchScreenKeyboard usernameKeyboard;
    private bool usernameKeyboardActive;
    private NeuroTapSideToPractice selectedSideToPractice;
    private NeuroTapDifficultyPreset selectedDifficulty;
    private int selectedSessionMinutes;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<NeuroTapStartMenuController>() != null)
            return;

        GameObject host = new GameObject("NeuroTapStartMenuController");
        host.AddComponent<NeuroTapStartMenuController>();
    }

    private void Start()
    {
        ResolveReferences();

        if (HasSceneStartScreenLayout())
            BindSceneStartScreen();
        else if (BuildMenuOnStart)
            BuildMenu();

        RefreshStatsPreview();

        if (NeuroCueVrUiInteractor.Instance != null)
            NeuroCueVrUiInteractor.Instance.RefreshRaycasters();
    }

    private void Update()
    {
        PollUsernameKeyboard();
    }

    public void BuildMenu()
    {
        ResolveReferences();

        if (StartScreen == null)
            return;

        if (ReplaceExistingStartScreenChildren)
            ClearChildren(StartScreen.transform);

        RectTransform root = EnsureRectTransform(StartScreen);
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(900f, 720f);
        root.anchoredPosition = Vector2.zero;

        AddLogo(root);

        GameObject panel = CreatePanel(root, "Menu Panel", new Vector2(0f, -80f), PanelSize, PanelColor);
        RectTransform panelRect = panel.GetComponent<RectTransform>();

        Button startButton = CreateButton(panelRect, "Start Game", new Vector2(0f, 150f), new Vector2(520f, 82f), PrimaryBlue, "Start Game", 31f);
        startButton.onClick.AddListener(StartNamedGame);

        Button guestButton = CreateButton(panelRect, "Guest Game", new Vector2(0f, 56f), new Vector2(520f, 58f), SecondaryBlue, "Guest Game", 24f);
        guestButton.onClick.AddListener(StartGuestGame);

        GameObject statsPreview = CreatePanel(panelRect, "Stats Preview", new Vector2(0f, -38f), new Vector2(520f, 70f), new Color(0.02f, 0.17f, 0.25f, 0.72f));
        statsPreviewText = CreateText(statsPreview.transform, "Stats Preview Text", Vector2.zero, new Vector2(500f, 58f), "", 21f, TextAlignmentOptions.Center);

        Button resultsButton = CreateButton(panelRect, "See My Results", new Vector2(-137f, -128f), new Vector2(246f, 56f), SecondaryBlue, "See My Results", 22f);
        resultsButton.onClick.AddListener(ShowResults);

        Button statsButton = CreateButton(panelRect, "My Stats", new Vector2(137f, -128f), new Vector2(246f, 56f), SecondaryBlue, "My Stats", 22f);
        statsButton.onClick.AddListener(ShowResults);

        Button exitButton = CreateButton(panelRect, "Exit", new Vector2(0f, -210f), new Vector2(240f, 64f), new Color(0f, 0f, 0f, 136f / 255f), "Exit", 26f);
        if (GameplayManager != null)
            exitButton.onClick.AddListener(GameplayManager.ExitGame);

        Button settingsButton = CreateCircularSettingsButton(root, new Vector2(392f, 286f), 68f);
        settingsButton.onClick.AddListener(ShowSettings);

        resultsPanel = BuildResultsPanel(root);
        resultsPanel.SetActive(false);

        settingsPanel = BuildSettingsPanel(root);
        LoadSettingsIntoControls();
        settingsPanel.SetActive(false);
    }

    private void StartNamedGame()
    {
        StartGame(guestSession: false);
    }

    private void StartGuestGame()
    {
        StartGame(guestSession: true);
    }

    private void StartGame(bool guestSession)
    {
        ResolveReferences();
        NeuroTapPatientProfile.ApplyToGameplay(GameplayManager, guestSession);

        if (GameplayManager != null)
            GameplayManager.StartGame();
    }

    private void ShowResults()
    {
        RefreshStatsPreview();
        RefreshResultsPanel();
        if (resultsPanel != null)
            resultsPanel.SetActive(true);
    }

    private void ShowSettings()
    {
        LoadSettingsIntoControls();
        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }

    private void SaveSettings()
    {
        CommitUsernameKeyboardText();

        if (usernameInput != null)
            NeuroTapPatientProfile.Username = usernameInput.text;
        NeuroTapPatientProfile.SideToPractice = selectedSideToPractice;
        NeuroTapPatientProfile.SessionMinutes = selectedSessionMinutes;
        NeuroTapPatientProfile.DifficultyPreset = selectedDifficulty;
        if (remindersToggle != null)
            NeuroTapPatientProfile.RemindersEnabled = remindersToggle.isOn;
        NeuroTapSessionReminderService.ApplyReminderPreference(NeuroTapPatientProfile.RemindersEnabled);

        NeuroTapPatientProfile.ApplyToGameplay(GameplayManager, guestSession: false);
        RefreshStatsPreview();

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    private void LoadSettingsIntoControls()
    {
        if (usernameInput != null)
            usernameInput.text = NeuroTapPatientProfile.Username;
        selectedSideToPractice = NeuroTapPatientProfile.SideToPractice;
        selectedSessionMinutes = NeuroTapPatientProfile.SessionMinutes;
        selectedDifficulty = NeuroTapPatientProfile.DifficultyPreset;
        RefreshSettingValueLabels();
        if (remindersToggle != null)
            remindersToggle.isOn = NeuroTapPatientProfile.RemindersEnabled;
    }

    private void RefreshStatsPreview()
    {
        if (statsPreviewText == null)
            return;

        string username = NeuroTapPatientProfile.Username;
        statsPreviewText.text =
            "Last: " + NeuroTapPatientProfile.GetLastScore(username) +
            "     Best: " + NeuroTapPatientProfile.GetBestScore(username) +
            "     Sessions: " + NeuroTapPatientProfile.GetSessionsCompleted(username);
    }

    private void RefreshResultsPanel()
    {
        if (resultsText == null)
            return;

        string username = NeuroTapPatientProfile.Username;
        resultsText.text =
            "User: " + username + "\n" +
            "Last Score: " + NeuroTapPatientProfile.GetLastScore(username) + "\n" +
            "Best Score: " + NeuroTapPatientProfile.GetBestScore(username) + "\n" +
            "Sessions Completed: " + NeuroTapPatientProfile.GetSessionsCompleted(username);
    }

    private GameObject BuildResultsPanel(Transform parent)
    {
        GameObject panel = CreatePanel(parent, "Results Panel", new Vector2(0f, -70f), new Vector2(620f, 360f), new Color(0.01f, 0.10f, 0.16f, 0.92f));
        CreateText(panel.transform, "Title", new Vector2(0f, 120f), new Vector2(560f, 52f), "My Results", 34f, TextAlignmentOptions.Center);

        resultsText = CreateText(
            panel.transform,
            "Results Text",
            new Vector2(0f, 20f),
            new Vector2(540f, 150f),
            "",
            25f,
            TextAlignmentOptions.Center);

        Button close = CreateButton(panel.transform, "Close Results", new Vector2(0f, -130f), new Vector2(250f, 54f), PrimaryBlue, "Close", 23f);
        close.onClick.AddListener(() => panel.SetActive(false));
        return panel;
    }

    private GameObject BuildSettingsPanel(Transform parent)
    {
        GameObject panel = CreatePanel(parent, "Settings Panel", new Vector2(0f, -70f), new Vector2(660f, 475f), new Color(0.01f, 0.10f, 0.16f, 0.94f));
        CreateText(panel.transform, "Title", new Vector2(0f, 194f), new Vector2(580f, 42f), "Settings", 32f, TextAlignmentOptions.Center);

        CreateText(panel.transform, "Username Label", new Vector2(-210f, 130f), new Vector2(180f, 34f), "Username", 21f, TextAlignmentOptions.Left);
        usernameInput = CreateInput(panel.transform, "Username Input", new Vector2(105f, 130f), new Vector2(350f, 44f));
        usernameInput.onSelect.AddListener(_ => OpenUsernameKeyboard());
        AddPointerClickHandler(usernameInput.gameObject, OpenUsernameKeyboard);

        CreateText(panel.transform, "Side Label", new Vector2(-210f, 70f), new Vector2(200f, 34f), "Side to Practice", 21f, TextAlignmentOptions.Left);
        Button sideButton = CreateButton(panel.transform, "Side To Practice Button", new Vector2(105f, 70f), new Vector2(350f, 44f), SecondaryBlue, "", 20f);
        sideValueText = sideButton.GetComponentInChildren<TMP_Text>();
        sideButton.onClick.AddListener(CycleSideToPractice);

        CreateText(panel.transform, "Duration Label", new Vector2(-210f, 10f), new Vector2(210f, 34f), "Session Duration", 21f, TextAlignmentOptions.Left);
        Button durationButton = CreateButton(panel.transform, "Session Duration Button", new Vector2(105f, 10f), new Vector2(350f, 44f), SecondaryBlue, "", 20f);
        sessionDurationValueText = durationButton.GetComponentInChildren<TMP_Text>();
        durationButton.onClick.AddListener(CycleSessionDuration);

        CreateText(panel.transform, "Difficulty Label", new Vector2(-210f, -50f), new Vector2(180f, 34f), "Difficulty", 21f, TextAlignmentOptions.Left);
        Button difficultyButton = CreateButton(panel.transform, "Difficulty Button", new Vector2(105f, -50f), new Vector2(350f, 44f), SecondaryBlue, "", 20f);
        difficultyValueText = difficultyButton.GetComponentInChildren<TMP_Text>();
        difficultyButton.interactable = true;
        difficultyButton.onClick.AddListener(CycleDifficulty);

        remindersToggle = CreateToggle(panel.transform, "Session Reminders Toggle", new Vector2(0f, -112f), "Session reminders");

        Button save = CreateButton(panel.transform, "Save Settings", new Vector2(-135f, -190f), new Vector2(250f, 54f), PrimaryBlue, "Save", 23f);
        save.onClick.AddListener(SaveSettings);

        Button cancel = CreateButton(panel.transform, "Cancel Settings", new Vector2(135f, -190f), new Vector2(250f, 54f), SecondaryBlue, "Cancel", 23f);
        cancel.onClick.AddListener(() => panel.SetActive(false));

        return panel;
    }

    private void CycleSideToPractice()
    {
        int next = ((int)selectedSideToPractice + 1) % 4;
        selectedSideToPractice = (NeuroTapSideToPractice)next;
        RefreshSettingValueLabels();
    }

    private void CycleSessionDuration()
    {
        if (selectedSessionMinutes == 3)
            selectedSessionMinutes = 5;
        else if (selectedSessionMinutes == 5)
            selectedSessionMinutes = 7;
        else
            selectedSessionMinutes = 3;

        RefreshSettingValueLabels();
    }

    private void CycleDifficulty()
    {
        int next = ((int)selectedDifficulty + 1) % 3;
        selectedDifficulty = (NeuroTapDifficultyPreset)next;
        RefreshSettingValueLabels();
    }

    private void RefreshSettingValueLabels()
    {
        if (sideValueText != null)
            sideValueText.text = NeuroTapPatientProfile.ToDisplaySideLabel(selectedSideToPractice);
        if (sessionDurationValueText != null)
            sessionDurationValueText.text = selectedSessionMinutes + " minutes";
        if (difficultyValueText != null)
            difficultyValueText.text = ToDifficultyLabel(selectedDifficulty);
    }

    private static string ToDifficultyLabel(NeuroTapDifficultyPreset difficulty)
    {
        switch (difficulty)
        {
            case NeuroTapDifficultyPreset.Easy:
                return "Easy";
            case NeuroTapDifficultyPreset.Hard:
                return "Hard";
            default:
                return "Medium";
        }
    }

    private void ResolveReferences()
    {
        if (GameplayManager == null)
            GameplayManager = GameplayManager.Instance != null ? GameplayManager.Instance : FindObjectOfType<GameplayManager>();

        if (MainMenuCanvas == null && GameplayManager != null)
            MainMenuCanvas = GameplayManager.MainMenuCanvas;

        if (MainMenuCanvas == null)
            MainMenuCanvas = GameObject.Find("Main Menu Canvas");

        if (StartScreen == null && GameplayManager != null)
            StartScreen = GameplayManager.StartScreen;

        if (StartScreen == null && MainMenuCanvas != null)
        {
            Transform existing = MainMenuCanvas.transform.Find("Start Screen");
            if (existing != null)
                StartScreen = existing.gameObject;
        }

        if (StartScreen == null && MainMenuCanvas != null)
        {
            StartScreen = new GameObject("Start Screen");
            StartScreen.layer = 5;
            StartScreen.transform.SetParent(MainMenuCanvas.transform, false);
        }

        if (GameplayManager != null && StartScreen != null)
            GameplayManager.StartScreen = StartScreen;
    }

    private bool HasSceneStartScreenLayout()
    {
        return StartScreen != null && StartScreen.transform.Find("Menu Panel") != null;
    }

    private void BindSceneStartScreen()
    {
        RectTransform root = EnsureRectTransform(StartScreen);
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(900f, 720f);
        root.anchoredPosition = Vector2.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;

        BindButton("Start Game", StartNamedGame);
        BindButton("Guest Game", StartGuestGame);
        BindButton("See My Results", ShowResults);
        BindButton("My Stats", ShowResults);
        BindButton("Settings Gear", ShowSettings);
        BindButton("Close Results", HideResultsPanel);
        BindButton("Save Settings", SaveSettings);
        BindButton("Cancel Settings", HideSettingsPanel);
        BindButton("Side To Practice Button", CycleSideToPractice);
        BindButton("Session Duration Button", CycleSessionDuration);
        BindButton("Difficulty Button", CycleDifficulty);

        if (GameplayManager != null)
            BindButton("Exit", GameplayManager.ExitGame);

        statsPreviewText = FindNamed<TMP_Text>("Stats Preview Text");
        resultsText = FindNamed<TMP_Text>("Results Text");
        resultsPanel = FindNamedObject("Results Panel");
        settingsPanel = FindNamedObject("Settings Panel");
        if (resultsPanel != null)
            resultsPanel.SetActive(false);
        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        usernameInput = FindNamed<TMP_InputField>("Username Input");
        if (usernameInput != null)
        {
            usernameInput.shouldHideMobileInput = false;
            usernameInput.onSelect.RemoveAllListeners();
            usernameInput.onSelect.AddListener(_ => OpenUsernameKeyboard());
            AddPointerClickHandler(usernameInput.gameObject, OpenUsernameKeyboard);
        }

        sideValueText = FindButtonLabel("Side To Practice Button");
        sessionDurationValueText = FindButtonLabel("Session Duration Button");
        difficultyValueText = FindButtonLabel("Difficulty Button");
        remindersToggle = FindNamed<Toggle>("Session Reminders Toggle");

        ApplyRuntimeSettingsGearIcon();
        LoadSettingsIntoControls();
    }

    private void ApplyRuntimeSettingsGearIcon()
    {
        Image gearImage = FindNamed<Image>("Settings Gear");
        if (gearImage == null)
            return;

        gearImage.sprite = BuildCircularGearSprite();
        gearImage.type = Image.Type.Simple;
        gearImage.preserveAspect = true;
        gearImage.color = Color.white;
        gearImage.enabled = true;
        gearImage.raycastTarget = true;
    }

    private void HideResultsPanel()
    {
        if (resultsPanel != null)
            resultsPanel.SetActive(false);
    }

    private void HideSettingsPanel()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    private void BindButton(string objectName, UnityEngine.Events.UnityAction action)
    {
        Button button = FindNamed<Button>(objectName);
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private TMP_Text FindButtonLabel(string buttonName)
    {
        Transform button = FindNamedTransform(buttonName);
        return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
    }

    private T FindNamed<T>(string objectName) where T : Component
    {
        Transform found = FindNamedTransform(objectName);
        return found != null ? found.GetComponent<T>() : null;
    }

    private GameObject FindNamedObject(string objectName)
    {
        Transform found = FindNamedTransform(objectName);
        return found != null ? found.gameObject : null;
    }

    private Transform FindNamedTransform(string objectName)
    {
        if (StartScreen == null)
            return null;

        Transform[] children = StartScreen.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == objectName)
                return children[i];
        }

        return null;
    }

    private void AddLogo(Transform parent)
    {
        Sprite sprite = LogoSprite != null ? LogoSprite : LoadLogoSprite();
        if (sprite == null)
        {
            CreateText(parent, "Logo Text", new Vector2(0f, 340f), new Vector2(1280f, 200f), "NEUROTAP", 112f, TextAlignmentOptions.Center);
            return;
        }

        GameObject logoObject = new GameObject("NeuroTap Logo");
        logoObject.layer = 5;
        logoObject.transform.SetParent(parent, false);

        RectTransform rect = logoObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1440f, 360f);
        rect.anchoredPosition = new Vector2(0f, 340f);

        Image image = logoObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private Sprite LoadLogoSprite()
    {
        Texture2D texture = Resources.Load<Texture2D>("NeuroTapLogo");
        if (texture == null)
            return null;

        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private GameObject CreatePanel(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.layer = 5;
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        Image image = panel.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;

        return panel;
    }

    private Button CreateButton(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, Color color, string label, float fontSize)
    {
        GameObject buttonObject = CreatePanel(parent, name, anchoredPosition, size, color);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        CreateText(buttonObject.transform, "Label", Vector2.zero, size, label, fontSize, TextAlignmentOptions.Center);
        return button;
    }

    /// <summary>
    /// Circular settings control — no emoji (TMP missing-glyph tofu) and no solid square plate.
    /// </summary>
    private Button CreateCircularSettingsButton(Transform parent, Vector2 anchoredPosition, float diameter)
    {
        GameObject buttonObject = new GameObject("Settings Gear");
        buttonObject.layer = 5;
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(diameter, diameter);
        rect.anchoredPosition = anchoredPosition;

        Image image = buttonObject.AddComponent<Image>();
        image.sprite = BuildCircularGearSprite();
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = Color.white;
        image.raycastTarget = true;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.85f, 0.95f, 1f, 1f);
        colors.pressedColor = new Color(0.7f, 0.85f, 0.95f, 1f);
        button.colors = colors;
        return button;
    }

    private static Sprite BuildCircularGearSprite()
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color fill = new Color(0.04f, 0.22f, 0.32f, 0.92f);
        Color rim = new Color(0.35f, 0.78f, 0.95f, 0.95f);
        Color icon = Color.white;

        float center = (size - 1) * 0.5f;
        float outerR = size * 0.48f;
        float rimInner = outerR - 3.5f;
        float gearOuter = size * 0.30f;
        float gearInner = size * 0.20f;
        float hubOuter = size * 0.10f;
        float hubInner = size * 0.045f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);

                if (dist > outerR)
                {
                    texture.SetPixel(x, y, clear);
                    continue;
                }

                // Soft circular plate + light rim (no square bounds visible).
                Color pixel = dist > rimInner ? rim : fill;

                // 8-tooth gear silhouette.
                float toothWave = Mathf.Cos(angle * 8f);
                float gearRadius = Mathf.Lerp(gearInner, gearOuter, toothWave > 0.35f ? 1f : 0f);
                if (toothWave > 0.15f && toothWave <= 0.35f)
                    gearRadius = Mathf.Lerp(gearInner, gearOuter, (toothWave - 0.15f) / 0.2f);

                bool inGear = dist <= gearRadius && dist >= hubInner;
                bool inHubRing = dist <= hubOuter && dist >= hubInner;
                if (inGear || inHubRing)
                    pixel = icon;

                // Keep center hole transparent-ish dark so it reads as a cog.
                if (dist < hubInner)
                    pixel = fill;

                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private TMP_Text CreateText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, string text, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name);
        textObject.layer = 5;
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enableWordWrapping = true;
        return label;
    }

    private TMP_InputField CreateInput(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject inputObject = CreatePanel(parent, name, anchoredPosition, size, new Color(0.03f, 0.19f, 0.28f, 0.9f));
        TMP_InputField input = inputObject.AddComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Alphanumeric;
        input.characterLimit = 32;
        input.shouldHideMobileInput = false;

        TMP_Text text = CreateText(inputObject.transform, "Text", Vector2.zero, size - new Vector2(24f, 0f), "", 21f, TextAlignmentOptions.MidlineLeft);
        TMP_Text placeholder = CreateText(inputObject.transform, "Placeholder", Vector2.zero, size - new Vector2(24f, 0f), "user001", 21f, TextAlignmentOptions.MidlineLeft);
        placeholder.color = new Color(1f, 1f, 1f, 0.45f);

        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = NeuroTapPatientProfile.Username;
        return input;
    }

    private static void AddPointerClickHandler(GameObject target, UnityEngine.Events.UnityAction action)
    {
        if (target == null || action == null)
            return;

        EventTrigger trigger = target.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = target.AddComponent<EventTrigger>();

        EventTrigger.Entry clickEntry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerClick
        };
        clickEntry.callback.AddListener(_ => action.Invoke());
        trigger.triggers.Add(clickEntry);
    }

    public void OpenUsernameKeyboard()
    {
        if (usernameInput == null)
            return;

        usernameInput.ActivateInputField();
        usernameKeyboard = TouchScreenKeyboard.Open(
            usernameInput.text,
            TouchScreenKeyboardType.Default,
            false,
            false,
            false,
            false,
            "Username",
            32);
        usernameKeyboardActive = usernameKeyboard != null;
    }

    private void PollUsernameKeyboard()
    {
        if (!usernameKeyboardActive || usernameKeyboard == null || usernameInput == null)
            return;

        usernameInput.text = usernameKeyboard.text;

        if (usernameKeyboard.status == TouchScreenKeyboard.Status.Done)
        {
            CommitUsernameKeyboardText();
            return;
        }

        if (usernameKeyboard.status == TouchScreenKeyboard.Status.Canceled ||
            usernameKeyboard.status == TouchScreenKeyboard.Status.LostFocus)
        {
            usernameKeyboardActive = false;
            usernameKeyboard = null;
            usernameInput.DeactivateInputField();
        }
    }

    private void CommitUsernameKeyboardText()
    {
        if (!usernameKeyboardActive || usernameKeyboard == null || usernameInput == null)
            return;

        usernameInput.text = usernameKeyboard.text;
        usernameKeyboardActive = false;
        usernameKeyboard = null;
        usernameInput.DeactivateInputField();
        NeuroTapPatientProfile.Username = usernameInput.text;
        if (GameplayManager != null)
            NeuroTapPatientProfile.ApplyToGameplay(GameplayManager, guestSession: false);
        RefreshStatsPreview();
    }

    private Toggle CreateToggle(Transform parent, string name, Vector2 anchoredPosition, string label)
    {
        GameObject toggleObject = new GameObject(name);
        toggleObject.layer = 5;
        toggleObject.transform.SetParent(parent, false);

        RectTransform rect = toggleObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(430f, 46f);
        rect.anchoredPosition = anchoredPosition;

        Toggle toggle = toggleObject.AddComponent<Toggle>();
        GameObject box = CreatePanel(toggleObject.transform, "Box", new Vector2(-195f, 0f), new Vector2(34f, 34f), new Color(0.03f, 0.19f, 0.28f, 0.95f));
        GameObject check = CreatePanel(box.transform, "Check", Vector2.zero, new Vector2(20f, 20f), PrimaryBlue);
        toggle.targetGraphic = box.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = NeuroTapPatientProfile.RemindersEnabled;

        CreateText(toggleObject.transform, "Label", new Vector2(35f, 0f), new Vector2(350f, 38f), label, 21f, TextAlignmentOptions.MidlineLeft);
        return toggle;
    }

    private static RectTransform EnsureRectTransform(GameObject target)
    {
        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect == null)
            rect = target.AddComponent<RectTransform>();
        return rect;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }
}
