using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Handles all in-game UI: dark background, consolidated header, tray card,
// animated score counter, floating score popups, and an elegant Game Over card.
// Attach to an empty GameObject named "UIManager" that lives inside the Canvas.
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD – wire up in Inspector")]
    public TMP_Text scoreValueText;   // large centered score number
    public TMP_Text bestValueText;    // smaller best score (top-left)

    [Header("Game Over Panel – wire up in Inspector")]
    public GameObject gameOverPanel;
    public TMP_Text   finalScoreText;
    public TMP_Text   finalBestText;
    public Button     restartButton;

    [Header("Visual Cleanup")]
    [SerializeField] bool hideDecorativeBars = true;
    [SerializeField] bool showLanguageButton = false;
    [SerializeField] bool enableVignetteOverlay = true;
    [SerializeField, Range(0.15f, 0.25f)] float vignetteOpacity = 0.18f;
    [SerializeField] Graphic logoGraphicForShadow;
    [SerializeField, Range(0.08f, 0.20f)] float logoShadowOpacity = 0.14f;
    [Header("Startup Splash")]
    [SerializeField] bool showStartupSplash = true;
    [SerializeField] bool showStartupSplashOnlyOncePerInstall = true;
    [SerializeField, Range(0.6f, 2f)] float startupSplashDuration = 1.6f;
    [SerializeField] Sprite startupLogoSprite;
    [SerializeField] string startupLogoResourcesPath = "Branding/culmin_studio_logo";
    [SerializeField, Range(0.6f, 2f)] float startupGameSplashDuration = 1.6f;
    [SerializeField] Sprite startupGameLogoSprite;
    [SerializeField] string startupGameLogoResourcesPath = "Branding/boomix_logo";
    [SerializeField] string startupSplashSeenKey = "startup_splash_seen_v1";
    [SerializeField] Color startupSplashBackgroundColor = Color.black;
    [SerializeField] string startupFallbackTitle = "CULMIN STUDIO";
    [SerializeField] string startupGameFallbackTitle = "BOOMIX\nBlock Puzzle";

    [Header("Best Icon")]
    [SerializeField] Sprite bestScoreIconSprite;
    [SerializeField] string bestScoreIconResourcesPath = "Branding/best_trophy_small";
    [SerializeField] Vector2 bestScoreIconSize = new Vector2(56f, 56f);
    [SerializeField] Sprite bestScoreLargeIconSprite;
    [SerializeField] string bestScoreLargeIconResourcesPath = "Branding/best_trophy_large";

    // ── Runtime-created elements ───────────────────────────────────────────────

    TMP_Text  _restartLabel;
    TMP_Text  _langLabel;            // language pill label (top-right header)
    Transform _uiRoot;               // SafeAreaRoot (fallback: canvas root)
    Transform _scorePopupLayer;      // parent for floating "+N" labels
    GameObject _gameOverCard;        // the centered glass card
    GameObject _dimOverlay;          // dark overlay behind card

    // Game Over extras
    CanvasGroup _panelGroup;         // drives fade-in and blocks raycasts
    GameObject  _newBestBadge;       // gold "NEW BEST!" pill (hidden unless record broken)
    bool        _newBestThisGame;    // true if this game's score beat the session-start high score
    int         _sessionStartHighScore; // high score recorded at Start(), before this game
    Coroutine   _finalScoreRoutine;  // count-up for final score display
    Transform   _confettiLayer;      // parent for confetti particles (new best only)

    // Score animation state
    int   _displayedScore;
    Coroutine _scoreCountRoutine;
    static Sprite _vignetteSprite;
    static Sprite _startupGradientSprite;
    static Sprite _bestScoreLargeIconCache;

    public static Sprite BestScoreLargeIcon => _bestScoreLargeIconCache;

    // ── Colors ────────────────────────────────────────────────────────────────

    static readonly Color BgColor     = new Color(0.059f, 0.059f, 0.071f, 1f);   // #0F0F12
    static readonly Color GlassColor  = new Color(1f, 1f, 1f, 0.09f);
    static readonly Color DimColor    = new Color(0f, 0f, 0f, 0.72f);            // ~0.72 alpha (req: 0.6–0.75)
    static readonly Color CardColor   = new Color(0.16f, 0.16f, 0.22f, 0.98f);
    static readonly Color PrimaryText = Color.white;
    static readonly Color SecondText  = new Color(1f, 1f, 1f, 0.70f);
    static readonly Color LabelText   = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color PopupColor  = new Color(1f, 0.92f, 0.23f);             // #FFEB3B
    static readonly Color BtnBgColor  = Color.white;
    static readonly Color BtnTextColor= new Color(0.059f, 0.059f, 0.071f, 1f);
    static readonly Color GoldColor   = new Color(1f, 0.84f, 0f, 1f);

    // Confetti burst colors (new best only)
    static readonly Color[] ConfettiColors =
    {
        new Color(1f, 0.84f, 0f),        // gold
        new Color(1f, 0.92f, 0.23f),     // yellow (matches popup)
        new Color(1f, 1f, 1f),           // white
        new Color(1f, 0.60f, 0.10f),     // orange
        new Color(0.85f, 1f, 0.35f),     // lime
    };

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;

        // Lock to portrait for all devices (iPhone 8+, all iPads).
        Screen.autorotateToPortrait            = true;
        Screen.autorotateToPortraitUpsideDown  = false;
        Screen.autorotateToLandscapeLeft       = false;
        Screen.autorotateToLandscapeRight      = false;
        Screen.orientation = ScreenOrientation.Portrait;

        // Set canvas to portrait reference BEFORE GridManager.Start() calls FitCamera.
        var canvas = GetComponentInParent<Canvas>() ?? FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);
                scaler.matchWidthOrHeight  = 0.75f; // height-weighted for better small-screen tray fit
            }

            var safeRoot = ApplySafeArea.GetOrCreateSafeAreaRoot(canvas);
            _uiRoot = safeRoot != null ? safeRoot : canvas.transform;
        }
    }

    // Top safe-area inset in 1920-tall canvas units.
    static float TopSafeInsetCanvas()
    {
        if (Screen.height <= 0) return 0f;
        float topPx = Screen.height - Screen.safeArea.yMax;
        return topPx * 1920f / Screen.height;
    }

    void OnEnable()
    {
        GameManager.OnScoreChanged     += HandleScoreChanged;
        GameManager.OnHighScoreChanged += HandleHighScoreChanged;
        GameManager.OnGameOver         += HandleGameOver;
        GameManager.OnScorePopup       += HandleScorePopup;
        LocalizationManager.OnLanguageChanged += RefreshTexts;
    }

    void OnDisable()
    {
        GameManager.OnScoreChanged     -= HandleScoreChanged;
        GameManager.OnHighScoreChanged -= HandleHighScoreChanged;
        GameManager.OnGameOver         -= HandleGameOver;
        GameManager.OnScorePopup       -= HandleScorePopup;
        LocalizationManager.OnLanguageChanged -= RefreshTexts;
    }

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>() ?? FindObjectOfType<Canvas>();
        if (_uiRoot == null && canvas != null)
        {
            var safeRoot = ApplySafeArea.GetOrCreateSafeAreaRoot(canvas);
            _uiRoot = safeRoot != null ? safeRoot : canvas.transform;
        }

        _bestScoreLargeIconCache = ResolveLogo(bestScoreLargeIconSprite, bestScoreLargeIconResourcesPath);

        // Remove leftover TMP_Text direct children from older UIManager layout
        RemoveLegacyTextChildren(canvas);

        // Build UI layers bottom-to-top
        BuildBackgroundVignette(canvas);
        CreatePopupLayer(canvas);
        BuildHeader(canvas);
        BuildTrayCard(canvas);
        ApplyLogoShadowIfPresent(canvas);
        if (ShouldShowStartupSplash()) StartCoroutine(ShowStartupSplash(canvas));

        // Game Over panel — configure BEFORE SetActive(false)
        ConfigureGameOverPanel();
        gameOverPanel.SetActive(false);

        // Wire restart button label
        _restartLabel = restartButton != null
            ? restartButton.GetComponentInChildren<TMP_Text>(true)
            : null;
        if (restartButton != null)
            restartButton.onClick.AddListener(() => GameManager.Instance.RestartGame());

        // Snapshot the high score that existed before this game starts.
        // Used at game-over to decide whether a new record was genuinely broken.
        _sessionStartHighScore = GameManager.Instance != null ? GameManager.Instance.HighScore : 0;

        RefreshTexts();
        HandleScoreChanged(GameManager.Instance != null ? GameManager.Instance.Score : 0);
        HandleHighScoreChanged(GameManager.Instance != null ? GameManager.Instance.HighScore : 0);
    }

    // ── Legacy cleanup ────────────────────────────────────────────────────────

    static void RemoveLegacyTextChildren(Canvas canvas)
    {
        if (canvas == null) return;
        var killList = new System.Collections.Generic.List<GameObject>();
        foreach (Transform child in canvas.transform)
            if (child.GetComponent<TextMeshProUGUI>() != null)
                killList.Add(child.gameObject);
        foreach (var go in killList) Destroy(go);
    }

    // ── Score popup layer ─────────────────────────────────────────────────────

    void BuildBackgroundVignette(Canvas canvas)
    {
        if (!enableVignetteOverlay || canvas == null) return;

        var root = GetUiRoot(canvas);
        var old = root.Find("BackgroundVignette");
        if (old != null) Destroy(old.gameObject);

        var go = new GameObject("BackgroundVignette");
        go.transform.SetParent(root, false);
        go.transform.SetSiblingIndex(0);

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.color = new Color(0f, 0f, 0f, Mathf.Clamp(vignetteOpacity, 0.15f, 0.25f));
        img.sprite = GetOrCreateVignetteSprite();
        img.type = Image.Type.Simple;
        img.preserveAspect = false;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void ApplyLogoShadowIfPresent(Canvas canvas)
    {
        Graphic target = logoGraphicForShadow;
        if (target == null && canvas != null)
        {
            var uiRoot = GetUiRoot(canvas);
            var graphics = uiRoot.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                if (g == null) continue;
                if (g.name.ToLowerInvariant().Contains("logo"))
                {
                    target = g;
                    break;
                }
            }
        }

        if (target == null) return;

        var shadow = target.GetComponent<Shadow>();
        if (shadow == null) shadow = target.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, Mathf.Clamp(logoShadowOpacity, 0.08f, 0.20f));
        shadow.effectDistance = new Vector2(0f, -4f);
        shadow.useGraphicAlpha = true;
    }

    static Sprite GetOrCreateVignetteSprite()
    {
        if (_vignetteSprite != null) return _vignetteSprite;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxDist = center.magnitude;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.45f) / 0.55f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, edge));
            }
        }

        tex.Apply(false, true);
        _vignetteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _vignetteSprite;
    }

    IEnumerator ShowStartupSplash(Canvas canvas)
    {
        if (canvas == null) yield break;

        var root = GetUiRoot(canvas);
        var existing = root.Find("StartupSplash");
        if (existing != null) Destroy(existing.gameObject);

        var splash = new GameObject("StartupSplash");
        splash.transform.SetParent(root, false);
        splash.transform.SetAsLastSibling();

        var bg = splash.AddComponent<Image>();
        bg.raycastTarget = true;
        bg.sprite = null;
        bg.color = startupSplashBackgroundColor;

        var splashRt = splash.GetComponent<RectTransform>();
        splashRt.anchorMin = Vector2.zero;
        splashRt.anchorMax = Vector2.one;
        splashRt.offsetMin = Vector2.zero;
        splashRt.offsetMax = Vector2.zero;

        var splashGroup = splash.AddComponent<CanvasGroup>();
        splashGroup.alpha = 1f;

        var firstContent = BuildSplashContent(splash.transform, ResolveStartupLogo(), startupFallbackTitle);
        yield return new WaitForSeconds(startupSplashDuration);

        var gameLogo = ResolveLogo(startupGameLogoSprite, startupGameLogoResourcesPath);
        if (gameLogo != null || !string.IsNullOrWhiteSpace(startupGameFallbackTitle))
        {
            var secondContent = BuildSplashContent(splash.transform, gameLogo, startupGameFallbackTitle);
            var secondCg = secondContent.GetComponent<CanvasGroup>();
            if (secondCg != null) secondCg.alpha = 0f;

            yield return CrossFadeSplashContent(firstContent, secondContent, 0.22f);
            Destroy(firstContent);

            yield return new WaitForSeconds(startupGameSplashDuration);
        }

        float t = 0f;
        const float fadeDur = 0.25f;
        while (t < fadeDur)
        {
            t += Time.deltaTime;
            splashGroup.alpha = 1f - Mathf.Clamp01(t / fadeDur);
            yield return null;
        }

        Destroy(splash);
        MarkStartupSplashSeen();
    }

    GameObject BuildSplashContent(Transform parent, Sprite logoSprite, string fallbackTitle)
    {
        var content = new GameObject("SplashContent");
        content.transform.SetParent(parent, false);
        var crt = content.AddComponent<RectTransform>();
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = Vector2.one;
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;
        content.AddComponent<CanvasGroup>().alpha = 1f;

        if (logoSprite != null)
        {
            var logoGo = new GameObject("SplashLogo");
            logoGo.transform.SetParent(content.transform, false);
            var logo = logoGo.AddComponent<Image>();
            logo.sprite = logoSprite;
            logo.preserveAspect = true;
            logo.raycastTarget = false;
            var shadow = logoGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.16f);
            shadow.effectDistance = new Vector2(0f, -4f);
            shadow.useGraphicAlpha = true;

            var lrt = logoGo.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0.5f, 0.5f);
            lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = new Vector2(0f, 20f);
            lrt.sizeDelta = new Vector2(700f, 700f);
        }
        else
        {
            var titleGo = new GameObject("SplashTitle");
            titleGo.transform.SetParent(content.transform, false);
            var title = titleGo.AddComponent<TextMeshProUGUI>();
            title.text = fallbackTitle;
            title.fontStyle = FontStyles.Bold;
            title.fontSize = 64f;
            title.alignment = TextAlignmentOptions.Center;
            title.color = new Color(1f, 1f, 1f, 0.92f);

            var trt = titleGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0.5f);
            trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(900f, 220f);
            trt.anchoredPosition = new Vector2(0f, 20f);
        }

        return content;
    }

    IEnumerator CrossFadeSplashContent(GameObject from, GameObject to, float duration)
    {
        if (from == null || to == null) yield break;

        var fromCg = from.GetComponent<CanvasGroup>();
        var toCg = to.GetComponent<CanvasGroup>();
        if (fromCg == null) fromCg = from.AddComponent<CanvasGroup>();
        if (toCg == null) toCg = to.AddComponent<CanvasGroup>();

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            fromCg.alpha = 1f - p;
            toCg.alpha = p;
            yield return null;
        }

        fromCg.alpha = 0f;
        toCg.alpha = 1f;
    }

    bool ShouldShowStartupSplash()
    {
        if (!showStartupSplash) return false;
        if (!showStartupSplashOnlyOncePerInstall) return true;
        return PlayerPrefs.GetInt(startupSplashSeenKey, 0) == 0;
    }

    void MarkStartupSplashSeen()
    {
        if (!showStartupSplashOnlyOncePerInstall) return;
        PlayerPrefs.SetInt(startupSplashSeenKey, 1);
        PlayerPrefs.Save();
    }

    Sprite ResolveStartupLogo()
    {
        return ResolveLogo(startupLogoSprite, startupLogoResourcesPath);
    }

    static Sprite ResolveLogo(Sprite overrideSprite, string resourcesPath)
    {
        if (overrideSprite != null) return overrideSprite;
        if (string.IsNullOrWhiteSpace(resourcesPath)) return null;

        var sprite = Resources.Load<Sprite>(resourcesPath);
        if (sprite != null) return sprite;

        var texture = Resources.Load<Texture2D>(resourcesPath);
        if (texture == null) return null;

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    static Sprite GetOrCreateStartupGradientSprite()
    {
        if (_startupGradientSprite != null) return _startupGradientSprite;

        const int w = 16;
        const int h = 256;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color top = new Color(0.06f, 0.07f, 0.09f, 1f);    // ~#0F1117
        Color bottom = new Color(0.10f, 0.12f, 0.17f, 1f); // ~#1A1F2B
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)(h - 1);
            Color c = Color.Lerp(bottom, top, t);
            for (int x = 0; x < w; x++) tex.SetPixel(x, y, c);
        }

        tex.Apply(false, true);
        _startupGradientSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return _startupGradientSprite;
    }

    void CreatePopupLayer(Canvas canvas)
    {
        var go = new GameObject("ScorePopupLayer");
        go.transform.SetParent(GetUiRoot(canvas), false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _scorePopupLayer = go.transform;
    }

    // ── Header ────────────────────────────────────────────────────────────────

    void BuildHeader(Canvas canvas)
    {
        var header = new GameObject("Header");
        header.transform.SetParent(GetUiRoot(canvas), false);

        var hrt = header.AddComponent<RectTransform>();
        hrt.anchorMin        = new Vector2(0f, 1f);
        hrt.anchorMax        = new Vector2(1f, 1f);
        hrt.pivot            = new Vector2(0.5f, 1f);
        hrt.anchoredPosition = new Vector2(0f, -ResponsiveLayoutController.HeaderTopPaddingPx());
        hrt.sizeDelta        = new Vector2(0f, ResponsiveLayoutController.HeaderHeightPx());

        var himg = header.AddComponent<Image>();
        himg.color         = new Color(1f, 1f, 1f, 0.03f);
        himg.raycastTarget = false;
        if (hideDecorativeBars) himg.enabled = false;

        // Best group — top-left (icon + best score in one row)
        var bestGroup = new GameObject("BestGroup");
        bestGroup.transform.SetParent(header.transform, false);
        bestGroup.AddComponent<RectTransform>();
        var bgrt = bestGroup.GetComponent<RectTransform>();
        bgrt.anchorMin        = new Vector2(0f, 1f);
        bgrt.anchorMax        = new Vector2(0f, 1f);
        bgrt.pivot            = new Vector2(0f, 1f);
        bgrt.anchoredPosition = new Vector2(20f, -20f);
        bgrt.sizeDelta        = new Vector2(260f, 56f);
        var bgLayout = bestGroup.AddComponent<HorizontalLayoutGroup>();
        bgLayout.childAlignment = TextAnchor.MiddleLeft;
        bgLayout.spacing = 8f;
        bgLayout.childForceExpandWidth = false;
        bgLayout.childForceExpandHeight = false;

        var bestIconSprite = ResolveLogo(bestScoreIconSprite, bestScoreIconResourcesPath);
        var bestIconGo = new GameObject("BestIcon");
        bestIconGo.transform.SetParent(bestGroup.transform, false);
        var bestIcon = bestIconGo.AddComponent<Image>();
        bestIcon.sprite = bestIconSprite;
        bestIcon.preserveAspect = true;
        bestIcon.raycastTarget = false;
        bestIcon.color = bestIconSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        var bestIconRt = bestIconGo.GetComponent<RectTransform>();
        bestIconRt.sizeDelta = bestScoreIconSize;
        var bestIconLayout = bestIconGo.AddComponent<LayoutElement>();
        const float bestIconH = 44f;
        bestIconLayout.minWidth = bestIconH;
        bestIconLayout.minHeight = bestIconH;
        bestIconLayout.preferredWidth = bestIconH;
        bestIconLayout.preferredHeight = bestIconH;

        bestValueText = MakeText(bestGroup.transform, "BestValue", "0", 44f, PrimaryText);
        bestValueText.fontStyle = FontStyles.Bold;
        bestValueText.enableAutoSizing = false;
        bestValueText.enableWordWrapping = false;
        bestValueText.overflowMode = TextOverflowModes.Overflow;
        bestValueText.fontSize = 40f;
        bestValueText.alignment = TextAlignmentOptions.MidlineLeft;
        var bestValueLayout = bestValueText.gameObject.AddComponent<LayoutElement>();
        bestValueLayout.minHeight = 44f;
        bestValueLayout.preferredHeight = 44f;
        bestValueLayout.minWidth = 160f;
        bestValueLayout.preferredWidth = 160f;

        // Score group — centered
        var scoreGroup = MakeVerticalGroup(header.transform, "ScoreGroup");
        var sgrt = scoreGroup.GetComponent<RectTransform>();
        sgrt.anchorMin        = new Vector2(0.5f, 1f);
        sgrt.anchorMax        = new Vector2(0.5f, 1f);
        sgrt.pivot            = new Vector2(0.5f, 1f);
        sgrt.anchoredPosition = new Vector2(0f, -24f);
        sgrt.sizeDelta        = new Vector2(260f, 0f);
        var sgLayout = scoreGroup.GetComponent<VerticalLayoutGroup>();
        sgLayout.childAlignment = TextAnchor.UpperCenter;
        sgLayout.spacing = 2f;

        scoreValueText = MakeText(scoreGroup.transform, "ScoreValue", "0", 62f, PrimaryText);
        scoreValueText.fontStyle = FontStyles.Bold;
        scoreValueText.alignment = TextAlignmentOptions.Center;
        scoreValueText.enableAutoSizing = false;

        if (showLanguageButton)
        {
            // Language toggle pill — top-right
            var langBtn = new GameObject("LangButton");
            langBtn.transform.SetParent(header.transform, false);
            var lbImg = langBtn.AddComponent<Image>();
            lbImg.color = new Color(1f, 1f, 1f, 0.12f);
            var lbBtn = langBtn.AddComponent<Button>();
            var lbColors = lbBtn.colors;
            lbColors.normalColor      = new Color(1f, 1f, 1f, 0.12f);
            lbColors.highlightedColor = new Color(1f, 1f, 1f, 0.22f);
            lbColors.pressedColor     = new Color(1f, 1f, 1f, 0.35f);
            lbBtn.colors = lbColors;
            lbBtn.onClick.AddListener(() => StartCoroutine(CycleLangAnimated()));
            var lbRt = langBtn.GetComponent<RectTransform>();
            lbRt.anchorMin        = new Vector2(1f, 0.5f);
            lbRt.anchorMax        = new Vector2(1f, 0.5f);
            lbRt.pivot            = new Vector2(1f, 0.5f);
            lbRt.anchoredPosition = new Vector2(-20f, 0f);
            lbRt.sizeDelta        = new Vector2(82f, 40f);

            var langLabelGo = new GameObject("LangLabel");
            langLabelGo.transform.SetParent(langBtn.transform, false);
            _langLabel            = langLabelGo.AddComponent<TextMeshProUGUI>();
            _langLabel.text       = LocalizationManager.Code[LocalizationManager.Current];
            _langLabel.fontSize   = 19f;
            _langLabel.fontStyle  = FontStyles.Bold;
            _langLabel.alignment  = TextAlignmentOptions.Center;
            _langLabel.color      = PrimaryText;
            var llRt = langLabelGo.GetComponent<RectTransform>();
            llRt.anchorMin = Vector2.zero;
            llRt.anchorMax = Vector2.one;
            llRt.offsetMin = Vector2.zero;
            llRt.offsetMax = Vector2.zero;
        }
        else
        {
            _langLabel = null;
        }

        // 1px divider at the bottom edge of the header strip
        var divider = new GameObject("HeaderDivider");
        divider.transform.SetParent(header.transform, false);
        var divImg = divider.AddComponent<Image>();
        divImg.color         = new Color(1f, 1f, 1f, 0.06f);
        divImg.raycastTarget = false;
        if (hideDecorativeBars) divImg.enabled = false;
        var divRt = divider.GetComponent<RectTransform>();
        divRt.anchorMin        = new Vector2(0f, 0f);
        divRt.anchorMax        = new Vector2(1f, 0f);
        divRt.pivot            = new Vector2(0.5f, 1f);
        divRt.anchoredPosition = Vector2.zero;
        divRt.sizeDelta        = new Vector2(0f, 1f);
    }

    // ── Tray glass card ───────────────────────────────────────────────────────

    void BuildTrayCard(Canvas canvas)
    {
        RemoveLegacyTrayCard(canvas);

        var root = new GameObject("TrayBackground");
        root.transform.SetParent(GetUiRoot(canvas), false);
        root.transform.SetSiblingIndex(1);

        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.offsetMin  = new Vector2(20f, 0f);
        rt.offsetMax  = new Vector2(-20f, 0f);
        float trayHeight = ResponsiveLayoutController.TrayHeightPx();
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(0f, trayHeight);

        // Layer 1: soft platform shadow (requested: TrayShadow)
        var shadow = CreateTrayLayer(root.transform, "TrayShadow", new Color(0f, 0f, 0f, 0.24f));
        var shadowRt = shadow.rectTransform;
        shadowRt.offsetMin = new Vector2(-10f, -12f);
        shadowRt.offsetMax = new Vector2(10f, 8f);

        // Layer 2: base background panel
        var bg = CreateTrayLayer(root.transform, "Background", new Color(1f, 1f, 1f, 0.060f));
        bg.rectTransform.offsetMin = Vector2.zero;
        bg.rectTransform.offsetMax = Vector2.zero;

        // Layer 3: inset/inner shadow to fake depth
        var inset = CreateTrayLayer(root.transform, "Inset", new Color(0f, 0f, 0f, 0.12f));
        inset.rectTransform.offsetMin = new Vector2(10f, 10f);
        inset.rectTransform.offsetMax = new Vector2(-10f, -10f);

        // Layer 4: subtle top strip highlight (requested: TrayHighlight)
        var hi = CreateTrayLayer(root.transform, "TrayHighlight", new Color(1f, 1f, 1f, 0.14f));
        var hiRt = hi.rectTransform;
        hiRt.anchorMin = new Vector2(0f, 1f);
        hiRt.anchorMax = new Vector2(1f, 1f);
        hiRt.pivot = new Vector2(0.5f, 1f);
        hiRt.anchoredPosition = Vector2.zero;
        hiRt.sizeDelta = new Vector2(0f, 30f);

        if (hideDecorativeBars)
        {
            shadow.enabled = false;
            bg.enabled = false;
            inset.enabled = false;
            hi.enabled = false;
        }

        var breath = root.AddComponent<TrayContainerBreath>();
        breath.scaleAmplitude = 0.005f;
        breath.cycleSeconds   = 3.2f;
    }

    float ComputeTrayBgHeight(Canvas canvas)
    {
        if (GridManager.Instance == null || TrayManager.Instance == null) return 80f;

        var gm = GridManager.Instance;
        var tm = TrayManager.Instance;

        float gridTop    = gm.origin.y + (gm.rows - 1) * gm.cellSize + gm.cellSize * 0.5f;
        float trayY      = tm.slotPositions.Length > 0 ? tm.slotPositions[0].y : gm.origin.y - 2.0f * gm.cellSize;
        float trayBottom = trayY - gm.cellSize;
        float viewBottom = trayBottom - 0.2f;
        float baseWorldH = gridTop - viewBottom;

        const float kCanvasH    = 1920f;
        const float kHeaderH    = 228f;   // ← must match GridManager
        float kGapPx            = ResponsiveLayoutController.HeaderToGridSpacingPx();
        float topSafeInset      = TopSafeInsetCanvas();
        float viewTopMargin     = baseWorldH * (topSafeInset + kHeaderH + kGapPx)
                                  / (kCanvasH - topSafeInset - kHeaderH - kGapPx);
        float totalWorld    = baseWorldH + viewTopMargin;

        float worldGridBottom = gm.origin.y - gm.cellSize * 0.5f;
        float distFromBottom  = worldGridBottom - viewBottom;
        float pxPerUnit       = kCanvasH / totalWorld;

        return Mathf.Max(distFromBottom * pxPerUnit - 3f, 20f);
    }

    float ComputeTrayPlatformHeight(Canvas canvas)
    {
        if (GridManager.Instance == null || TrayManager.Instance == null) return 160f;
        float pxPerUnit = ComputeCanvasPixelsPerWorldUnit();
        float worldH = GridManager.Instance.cellSize * TrayManager.Instance.trayBlockScale * 2.5f;
        return Mathf.Clamp(worldH * pxPerUnit, 120f, 240f);
    }

    float ComputeTrayPlatformBottom(Canvas canvas)
    {
        if (GridManager.Instance == null || TrayManager.Instance == null || Camera.main == null)
            return 26f;

        var tm = TrayManager.Instance;
        var gm = GridManager.Instance;

        float trayY = tm.slotPositions.Length > 0 ? tm.slotPositions[0].y : gm.origin.y - 2.0f * gm.cellSize;
        float trayBottomWorld = trayY - gm.cellSize * tm.trayBlockScale * 1.10f;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(new Vector3(0f, trayBottomWorld, 0f));
        float canvasY = screenPos.y * 1920f / Mathf.Max(Screen.height, 1);
        return Mathf.Max(10f, canvasY);
    }

    float ComputeCanvasPixelsPerWorldUnit()
    {
        if (GridManager.Instance == null || TrayManager.Instance == null) return 170f;

        var gm = GridManager.Instance;
        var tm = TrayManager.Instance;

        float gridTop    = gm.origin.y + (gm.rows - 1) * gm.cellSize + gm.cellSize * 0.5f;
        float trayY      = tm.slotPositions.Length > 0 ? tm.slotPositions[0].y : gm.origin.y - 2.0f * gm.cellSize;
        float trayBottom = trayY - gm.cellSize;
        float viewBottom = trayBottom - 0.2f;
        float baseWorldH = gridTop - viewBottom;

        const float kCanvasH = 1920f;
        const float kHeaderH = 228f;
        float kGapPx         = ResponsiveLayoutController.HeaderToGridSpacingPx();
        float topSafeInset   = TopSafeInsetCanvas();
        float viewTopMargin  = baseWorldH * (topSafeInset + kHeaderH + kGapPx)
                               / (kCanvasH - topSafeInset - kHeaderH - kGapPx);
        float totalWorld = baseWorldH + viewTopMargin;
        return kCanvasH / Mathf.Max(totalWorld, 0.001f);
    }

    void ComputeTrayAnchors(out float minX, out float maxX)
    {
        minX = 0.14f;
        maxX = 0.86f;

        if (TrayManager.Instance == null || Camera.main == null || TrayManager.Instance.slotPositions.Length == 0)
            return;

        var slots = TrayManager.Instance.slotPositions;
        float left = Camera.main.WorldToViewportPoint(slots[0]).x;
        float right = Camera.main.WorldToViewportPoint(slots[slots.Length - 1]).x;
        if (left > right) { float tmp = left; left = right; right = tmp; }

        minX = Mathf.Clamp01(left - 0.14f);
        maxX = Mathf.Clamp01(right + 0.14f);

        if (maxX - minX < 0.56f)
        {
            float c = (minX + maxX) * 0.5f;
            minX = Mathf.Clamp01(c - 0.28f);
            maxX = Mathf.Clamp01(c + 0.28f);
        }
    }

    static Image CreateTrayLayer(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return img;
    }

    static void RemoveLegacyTrayCard(Canvas canvas)
    {
        if (canvas == null) return;
        var old = canvas.transform.Find("TrayBackground");
        if (old != null) Destroy(old.gameObject);
        var safe = canvas.transform.Find("SafeAreaRoot");
        if (safe != null)
        {
            var safeTray = safe.Find("TrayBackground");
            if (safeTray != null) Destroy(safeTray.gameObject);
        }
        var tray = canvas.transform.Find("TrayContainer"); // legacy runtime name
        if (tray != null) Destroy(tray.gameObject);
    }

    Transform GetUiRoot(Canvas canvas)
    {
        if (_uiRoot != null) return _uiRoot;
        if (canvas == null) return transform;
        var safeRoot = ApplySafeArea.GetOrCreateSafeAreaRoot(canvas);
        _uiRoot = safeRoot != null ? safeRoot : canvas.transform;
        return _uiRoot;
    }

    // ── Game Over panel ───────────────────────────────────────────────────────

    void ConfigureGameOverPanel()
    {
        if (gameOverPanel == null) return;

        // Remove orphaned text children from older layouts
        var orphans = new System.Collections.Generic.List<GameObject>();
        foreach (Transform child in gameOverPanel.transform)
            if (child.GetComponent<TextMeshProUGUI>() != null &&
                child.gameObject != (restartButton != null ? restartButton.gameObject : null))
                orphans.Add(child.gameObject);
        foreach (var o in orphans) Destroy(o);

        // Full-screen transparent root — children render the visuals
        var panelRt = gameOverPanel.GetComponent<RectTransform>();
        panelRt.anchorMin        = Vector2.zero;
        panelRt.anchorMax        = Vector2.one;
        panelRt.pivot            = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta        = Vector2.zero;

        var panelImg = gameOverPanel.GetComponent<Image>();
        if (panelImg == null) panelImg = gameOverPanel.AddComponent<Image>();
        panelImg.color         = Color.clear;
        panelImg.raycastTarget = false;

        // CanvasGroup: controls fade-in alpha and blocks raycasts after animation
        _panelGroup = gameOverPanel.GetComponent<CanvasGroup>();
        if (_panelGroup == null) _panelGroup = gameOverPanel.AddComponent<CanvasGroup>();
        _panelGroup.alpha          = 0f;
        _panelGroup.interactable   = false;
        _panelGroup.blocksRaycasts = false;

        // ── Dim overlay (full-screen dark layer) ──────────────────────────────
        _dimOverlay = new GameObject("DimOverlay");
        _dimOverlay.transform.SetParent(gameOverPanel.transform, false);
        var dimImg = _dimOverlay.AddComponent<Image>();
        dimImg.color         = DimColor;      // black at 0.72 alpha
        dimImg.raycastTarget = true;          // blocks taps to game board area
        var dimRt = _dimOverlay.GetComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;

        // ── Centered glass card ───────────────────────────────────────────────
        // 600×520 canvas units — ~56% of 1080-wide canvas, 27% of height
        _gameOverCard = new GameObject("Card");
        _gameOverCard.transform.SetParent(gameOverPanel.transform, false);
        var cardImg = _gameOverCard.AddComponent<Image>();
        cardImg.color = CardColor;
        var cardRt = _gameOverCard.GetComponent<RectTransform>();
        cardRt.anchorMin        = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax        = new Vector2(0.5f, 0.5f);
        cardRt.pivot            = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = Vector2.zero;
        cardRt.sizeDelta        = new Vector2(600f, 520f);

        // "GAME OVER" title — small, uppercase, subdued (lets score be the hero)
        var titleGo  = new GameObject("GameOverTitle");
        titleGo.transform.SetParent(_gameOverCard.transform, false);
        var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        titleTmp.text             = LocalizationManager.Get(LocalizationManager.Key.GameOver);
        titleTmp.fontSize         = 22f;
        titleTmp.fontStyle        = FontStyles.Bold;
        titleTmp.alignment        = TextAlignmentOptions.Center;
        titleTmp.color            = new Color(1f, 1f, 1f, 0.50f);
        titleTmp.characterSpacing = 12f;
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin        = new Vector2(0f, 1f);
        titleRt.anchorMax        = new Vector2(1f, 1f);
        titleRt.pivot            = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -34f);
        titleRt.sizeDelta        = new Vector2(0f, 52f);

        // Final score value — hero text at 88px
        var fsGo = new GameObject("FinalScoreValue");
        fsGo.transform.SetParent(_gameOverCard.transform, false);
        finalScoreText           = fsGo.AddComponent<TextMeshProUGUI>();
        finalScoreText.text      = "0";
        finalScoreText.fontSize  = 88f;
        finalScoreText.fontStyle = FontStyles.Bold;
        finalScoreText.alignment = TextAlignmentOptions.Center;
        finalScoreText.color     = PrimaryText;
        var fsRt = fsGo.GetComponent<RectTransform>();
        fsRt.anchorMin        = new Vector2(0f, 1f);
        fsRt.anchorMax        = new Vector2(1f, 1f);
        fsRt.pivot            = new Vector2(0.5f, 1f);
        fsRt.anchoredPosition = new Vector2(0f, -104f);
        fsRt.sizeDelta        = new Vector2(0f, 108f);

        // "NEW BEST!" gold pill badge — hidden until a record is broken
        _newBestBadge = new GameObject("NewBestBadge");
        _newBestBadge.transform.SetParent(_gameOverCard.transform, false);
        var badgeImg = _newBestBadge.AddComponent<Image>();
        badgeImg.color = GoldColor;
        var badgeRt = _newBestBadge.GetComponent<RectTransform>();
        badgeRt.anchorMin        = new Vector2(0.5f, 1f);
        badgeRt.anchorMax        = new Vector2(0.5f, 1f);
        badgeRt.pivot            = new Vector2(0.5f, 1f);
        badgeRt.anchoredPosition = new Vector2(0f, -220f);
        badgeRt.sizeDelta        = new Vector2(220f, 42f);

        var badgeLabelGo = new GameObject("BadgeLabel");
        badgeLabelGo.transform.SetParent(_newBestBadge.transform, false);
        var badgeTmp              = badgeLabelGo.AddComponent<TextMeshProUGUI>();
        badgeTmp.text             = "NEW BEST!";
        badgeTmp.fontSize         = 16f;
        badgeTmp.fontStyle        = FontStyles.Bold;
        badgeTmp.alignment        = TextAlignmentOptions.Center;
        badgeTmp.color            = BtnTextColor;   // dark on gold
        badgeTmp.characterSpacing = 10f;
        var badgeLabelRt = badgeLabelGo.GetComponent<RectTransform>();
        badgeLabelRt.anchorMin = Vector2.zero;
        badgeLabelRt.anchorMax = Vector2.one;
        badgeLabelRt.offsetMin = Vector2.zero;
        badgeLabelRt.offsetMax = Vector2.zero;

        _newBestBadge.SetActive(false);   // hidden by default

        // Best row (label + value) — always shown
        var bestRow = new GameObject("BestRow");
        bestRow.transform.SetParent(_gameOverCard.transform, false);
        var bestRowRt = bestRow.AddComponent<RectTransform>();
        bestRowRt.anchorMin        = new Vector2(0.5f, 1f);
        bestRowRt.anchorMax        = new Vector2(0.5f, 1f);
        bestRowRt.pivot            = new Vector2(0.5f, 1f);
        bestRowRt.anchoredPosition = new Vector2(0f, -278f);
        bestRowRt.sizeDelta        = new Vector2(300f, 40f);
        var hlg = bestRow.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.spacing                = 10f;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var bestLbl = new GameObject("BestIcon");
        bestLbl.transform.SetParent(bestRow.transform, false);
        var bestRowIcon = bestLbl.AddComponent<Image>();
        var bestRowIconSprite = ResolveLogo(bestScoreIconSprite, bestScoreIconResourcesPath);
        bestRowIcon.sprite = bestRowIconSprite;
        bestRowIcon.preserveAspect = true;
        bestRowIcon.raycastTarget = false;
        bestRowIcon.color = bestRowIconSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        var blSd = bestLbl.AddComponent<LayoutElement>();
        blSd.preferredWidth  = 40f;
        blSd.preferredHeight = 40f;

        var bestVal = new GameObject("BestValue");
        bestVal.transform.SetParent(bestRow.transform, false);
        finalBestText           = bestVal.AddComponent<TextMeshProUGUI>();
        finalBestText.text      = "0";
        finalBestText.fontSize  = 26f;
        finalBestText.fontStyle = FontStyles.Bold;
        finalBestText.color     = SecondText;
        finalBestText.alignment = TextAlignmentOptions.Center;
        var bvSd = bestVal.AddComponent<LayoutElement>();
        bvSd.preferredWidth  = 200f;
        bvSd.preferredHeight = 40f;

        // Horizontal divider
        var divider = new GameObject("Divider");
        divider.transform.SetParent(_gameOverCard.transform, false);
        var divImg = divider.AddComponent<Image>();
        divImg.color = new Color(1f, 1f, 1f, 0.08f);
        var divRt = divider.GetComponent<RectTransform>();
        divRt.anchorMin        = new Vector2(0.1f, 1f);
        divRt.anchorMax        = new Vector2(0.9f, 1f);
        divRt.pivot            = new Vector2(0.5f, 1f);
        divRt.anchoredPosition = new Vector2(0f, -334f);
        divRt.sizeDelta        = new Vector2(0f, 1f);

        // ── Primary CTA: "Tekrar Oyna" ────────────────────────────────────────
        // 420×72 — large touch target, white pill with dark text
        if (restartButton != null)
        {
            restartButton.transform.SetParent(_gameOverCard.transform, false);

            var btnImg = restartButton.GetComponent<Image>();
            if (btnImg == null) btnImg = restartButton.gameObject.AddComponent<Image>();
            btnImg.color = BtnBgColor;

            var btnColors = restartButton.colors;
            btnColors.normalColor      = BtnBgColor;
            btnColors.highlightedColor = new Color(0.92f, 0.92f, 0.92f);
            btnColors.pressedColor     = new Color(0.80f, 0.80f, 0.80f);
            restartButton.colors = btnColors;

            var btnRt = restartButton.GetComponent<RectTransform>();
            btnRt.anchorMin        = new Vector2(0.5f, 0f);
            btnRt.anchorMax        = new Vector2(0.5f, 0f);
            btnRt.pivot            = new Vector2(0.5f, 0f);
            btnRt.anchoredPosition = new Vector2(0f, 96f);
            btnRt.sizeDelta        = new Vector2(420f, 72f);
        }

        // ── Confetti layer (above card, for New Best burst) ───────────────────
        var confettiGo = new GameObject("ConfettiLayer");
        confettiGo.transform.SetParent(gameOverPanel.transform, false);
        var confettiRt = confettiGo.AddComponent<RectTransform>();
        confettiRt.anchorMin = Vector2.zero;
        confettiRt.anchorMax = Vector2.one;
        confettiRt.offsetMin = Vector2.zero;
        confettiRt.offsetMax = Vector2.zero;
        _confettiLayer = confettiGo.transform;
    }

    // ── Localization ──────────────────────────────────────────────────────────

    void RefreshTexts()
    {
        if (_restartLabel)
        {
            _restartLabel.text      = LocalizationManager.Get(LocalizationManager.Key.PlayAgain);
            _restartLabel.color     = BtnTextColor;
            _restartLabel.fontStyle = FontStyles.Bold;
            _restartLabel.fontSize  = 18f;
        }

        if (_langLabel)
            _langLabel.text = LocalizationManager.Code[LocalizationManager.Current];

        HandleScoreChanged(GameManager.Instance != null ? GameManager.Instance.Score : 0);
        HandleHighScoreChanged(GameManager.Instance != null ? GameManager.Instance.HighScore : 0);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    void HandleScoreChanged(int score)
    {
        if (_scoreCountRoutine != null) StopCoroutine(_scoreCountRoutine);
        _scoreCountRoutine = StartCoroutine(CountUpScore(_displayedScore, score));
    }

    void HandleHighScoreChanged(int high)
    {
        if (bestValueText)
            bestValueText.text = high.ToString("N0");
    }

    void HandleGameOver()
    {
        if (gameOverPanel == null) return;

        int score = GameManager.Instance != null ? GameManager.Instance.Score     : 0;
        int best  = GameManager.Instance != null ? GameManager.Instance.HighScore : 0;

        // Set best score text immediately; final score will count up
        if (finalScoreText) finalScoreText.text = "0";
        if (finalBestText)  finalBestText.text  = best.ToString("N0");

        // New best only if this game's score strictly exceeds the pre-game high score
        _newBestThisGame = score > _sessionStartHighScore;

        // Show/hide "NEW BEST!" badge
        if (_newBestBadge != null)
            _newBestBadge.SetActive(_newBestThisGame);

        // Reset panel: transparent and non-interactive until animation runs
        if (_panelGroup != null)
        {
            _panelGroup.alpha          = 0f;
            _panelGroup.interactable   = false;
            _panelGroup.blocksRaycasts = false;
        }

        // Reset card scale for spring animation
        if (_gameOverCard != null)
            _gameOverCard.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        // Render above header, tray card, and gameplay
        gameOverPanel.transform.SetAsLastSibling();
        gameOverPanel.SetActive(true);

        // Kick off animations
        if (_finalScoreRoutine != null) StopCoroutine(_finalScoreRoutine);
        _finalScoreRoutine = StartCoroutine(CountUpFinalScore(score));

        StartCoroutine(AnimateGameOverPanel());

        // Confetti only on new best
        if (_newBestThisGame && _confettiLayer != null)
            StartCoroutine(SpawnConfetti());
    }

    void HandleScorePopup(int delta, Vector3 worldPos)
    {
        if (_scorePopupLayer == null || Camera.main == null) return;

        var canvas = GetComponentInParent<Canvas>() ?? FindObjectOfType<Canvas>();
        if (canvas == null) return;

        Vector2 screenPt = Camera.main.WorldToScreenPoint(worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.GetComponent<RectTransform>(), screenPt,
            canvas.worldCamera, out Vector2 localPt);

        StartCoroutine(SpawnPopup(delta, localPt));
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    IEnumerator CountUpScore(int from, int to)
    {
        const float duration = 0.30f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            int v = Mathf.RoundToInt(Mathf.Lerp(from, to, elapsed / duration));
            if (scoreValueText) scoreValueText.text = v.ToString("N0");
            yield return null;
        }
        if (scoreValueText) scoreValueText.text = to.ToString("N0");
        _displayedScore = to;
    }

    // Count up the final score on the Game Over panel.
    // Waits briefly so the panel is visible before animating.
    IEnumerator CountUpFinalScore(int target)
    {
        if (finalScoreText == null) yield break;
        finalScoreText.text = "0";

        // Let the panel fade in before counting starts
        yield return new WaitForSeconds(0.30f);

        const float duration = 1.10f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t    = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 2.5f);   // ease-out power curve
            int   v    = Mathf.RoundToInt(Mathf.Lerp(0f, target, ease));
            finalScoreText.text = v.ToString("N0");
            yield return null;
        }
        finalScoreText.text = target.ToString("N0");

        // Punch-scale the badge after the score lands, for extra dopamine
        if (_newBestThisGame && _newBestBadge != null && _newBestBadge.activeSelf)
            StartCoroutine(PulseBadge());
    }

    // Fade in CanvasGroup + spring-scale card: 0.9 → 1.04 → 1.0
    IEnumerator AnimateGameOverPanel()
    {
        if (_panelGroup == null || _gameOverCard == null) yield break;

        const float fadeDur  = 0.25f;   // CanvasGroup alpha 0→1
        const float scaleDur = 0.42f;   // card spring scale

        float elapsed = 0f;
        float maxDur  = Mathf.Max(fadeDur, scaleDur);

        while (elapsed < maxDur)
        {
            elapsed += Time.deltaTime;

            // Alpha fade
            _panelGroup.alpha = Mathf.Clamp01(elapsed / fadeDur);

            // Spring scale: ramp up to 1.04 then settle to 1.0
            float st = Mathf.Clamp01(elapsed / scaleDur);
            float s  = st < 0.70f
                ? Mathf.Lerp(0.90f, 1.04f, st / 0.70f)          // expand
                : Mathf.Lerp(1.04f, 1.00f, (st - 0.70f) / 0.30f); // settle

            _gameOverCard.transform.localScale = new Vector3(s, s, 1f);
            yield return null;
        }

        _panelGroup.alpha                  = 1f;
        _gameOverCard.transform.localScale = Vector3.one;

        // Enable interactions after animation completes
        _panelGroup.interactable   = true;
        _panelGroup.blocksRaycasts = true;
    }

    // Punch-scale the "NEW BEST!" badge after the score count-up finishes
    IEnumerator PulseBadge()
    {
        if (_newBestBadge == null) yield break;
        var rt = _newBestBadge.GetComponent<RectTransform>();
        if (rt == null) yield break;

        const float duration = 0.30f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float s = t < 0.5f
                ? Mathf.Lerp(1f, 1.18f, t / 0.5f)
                : Mathf.Lerp(1.18f, 1f, (t - 0.5f) / 0.5f);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    // Confetti burst — only triggered on New Best
    IEnumerator SpawnConfetti()
    {
        if (_confettiLayer == null) yield break;

        // Slight delay to sync with panel becoming visible
        yield return new WaitForSeconds(0.35f);

        // Spawn 28 particles in a staggered burst
        const int count = 28;
        for (int i = 0; i < count; i++)
        {
            Color col   = ConfettiColors[Random.Range(0, ConfettiColors.Length)];
            // Mostly upward arc (30°–150°) with varied speed
            float angle = Random.Range(25f, 155f) * Mathf.Deg2Rad;
            float speed = Random.Range(260f, 540f);
            var   vel   = new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed);
            float delay = Random.Range(0f, 0.10f);   // stagger the burst
            StartCoroutine(SpawnOneConfetti(col, vel, delay));
        }

        yield return null;
    }

    IEnumerator SpawnOneConfetti(Color color, Vector2 velocity, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (_confettiLayer == null) yield break;

        var go = new GameObject("Confetti");
        go.transform.SetParent(_confettiLayer, false);

        var img   = go.AddComponent<Image>();
        img.color = color;

        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(Random.Range(-100f, 100f), Random.Range(-20f, 40f));
        rt.sizeDelta        = new Vector2(Random.Range(12f, 22f), Random.Range(7f, 13f));
        rt.rotation         = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        const float gravity  = -900f;
        const float duration = 0.88f;
        float elapsed = 0f;
        Vector2 vel   = velocity;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            vel.y               += gravity * Time.deltaTime;
            rt.anchoredPosition += vel * Time.deltaTime;
            // Slow spin while in flight
            rt.Rotate(0f, 0f, vel.x * 0.04f * Time.deltaTime * 60f);
            // Fade out over the last 40 % of lifetime
            float t     = elapsed / duration;
            float alpha = t < 0.60f ? 1f : Mathf.InverseLerp(1f, 0.60f, t);
            img.color   = new Color(color.r, color.g, color.b, alpha);
            yield return null;
        }

        Destroy(go);
    }

    IEnumerator SpawnPopup(int delta, Vector2 canvasPos)
    {
        var go = new GameObject("ScorePopup");
        go.transform.SetParent(_scorePopupLayer, false);

        // Font boyutu delta büyüklüğüne göre ölçeklenir
        float fontSize = delta >= 300 ? 72f : delta >= 100 ? 62f : 52f;

        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = "+" + delta.ToString("N0");
        tmp.fontSize  = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = PopupColor;
        tmp.alignment = TextAlignmentOptions.Center;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(320f, 100f);
        rt.anchoredPosition = canvasPos;

        // Pop-in: küçükten büyüğe sıçrama
        rt.localScale = Vector3.one * 0.4f;
        float popDur  = 0.12f;
        float popT    = 0f;
        while (popT < popDur)
        {
            popT += Time.deltaTime;
            float s = Mathf.Lerp(0.4f, 1.1f, popT / popDur);
            rt.localScale = Vector3.one * s;
            yield return null;
        }
        popT = 0f;
        while (popT < 0.06f)
        {
            popT += Time.deltaTime;
            float s = Mathf.Lerp(1.1f, 1f, popT / 0.06f);
            rt.localScale = Vector3.one * s;
            yield return null;
        }
        rt.localScale = Vector3.one;

        // Yukarı yüksel ve kaybol
        const float duration = 0.90f;
        float elapsed = 0f;
        Vector2 startPos = canvasPos;
        Vector2 endPos   = canvasPos + new Vector2(0f, 200f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t    = elapsed / duration;
            float ease = 1f - Mathf.Pow(1f - t, 2f);   // ease-out: hızlı çıkış, yavaş bitiş
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
            // Son %40'ta soluklaş
            float alpha = t < 0.60f ? 1f : Mathf.InverseLerp(1f, 0.60f, t);
            tmp.color = new Color(PopupColor.r, PopupColor.g, PopupColor.b, alpha);
            yield return null;
        }

        Destroy(go);
    }

    // ── BIG BANG efekti (büyük clear) ─────────────────────────────────────────

    // tier: 1=BIG BANG (x2), 2=MEGA BANG (x3), 3=ULTRA BANG (x4)
    public void ShowBigClear(int tier = 1)
    {
        StartCoroutine(BigClearAnimation(tier));
    }

    IEnumerator BigClearAnimation(int tier)
    {
        var canvas = GetComponentInParent<Canvas>() ?? FindObjectOfType<Canvas>();
        if (canvas == null) yield break;

        // Tahtanın dünya ortasını canvas koordinatına çevir
        Vector2 boardCanvasPos = Vector2.zero;
        if (GridManager.Instance != null && Camera.main != null)
        {
            var g = GridManager.Instance;
            var center = new Vector3(
                g.origin.x + (g.columns - 1) * g.cellSize * 0.5f,
                g.origin.y + (g.rows    - 1) * g.cellSize * 0.5f,
                0f);
            Vector2 screenPt = Camera.main.WorldToScreenPoint(center);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(), screenPt,
                canvas.worldCamera, out boardCanvasPos);
        }

        StartCoroutine(BigScreenFlash());
        StartCoroutine(BigCameraShake(0.30f));
        yield return StartCoroutine(BigBangText(boardCanvasPos, tier));
    }

    // Altın-sarı ekran flaşı
    IEnumerator BigScreenFlash()
    {
        if (_scorePopupLayer == null) yield break;

        var go  = new GameObject("BigFlash");
        go.transform.SetParent(_scorePopupLayer, false);
        go.transform.SetAsFirstSibling(); // içeriklerin arkasında kalsın

        var img = go.AddComponent<Image>();
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Hızlı alevlenme: 0 → 0.55, sonra yavaş sönme
        Color flashCol = new Color(1f, 0.85f, 0.10f); // altın
        const float inDur  = 0.07f;
        const float outDur = 0.22f;

        float t = 0f;
        while (t < inDur)
        {
            t += Time.deltaTime;
            img.color = new Color(flashCol.r, flashCol.g, flashCol.b,
                                  Mathf.Lerp(0f, 0.55f, t / inDur));
            yield return null;
        }
        t = 0f;
        while (t < outDur)
        {
            t += Time.deltaTime;
            img.color = new Color(flashCol.r, flashCol.g, flashCol.b,
                                  Mathf.Lerp(0.55f, 0f, t / outDur));
            yield return null;
        }

        Destroy(go);
    }

    // Kamera sarsıntısı
    IEnumerator BigCameraShake(float duration)
    {
        if (Camera.main == null) yield break;
        var   cam      = Camera.main.transform;
        Vector3 origin = cam.position;
        float cs       = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float strength = cs * 0.07f * (1f - elapsed / duration); // giderek azalan şiddet
            Vector2 offset = Random.insideUnitCircle * strength;
            cam.position = origin + new Vector3(offset.x, offset.y, 0f);
            yield return null;
        }

        cam.position = origin; // tam orijine döndür
    }

    // Tier'e göre "BIG BANG!" / "MEGA BANG!" / "ULTRA BANG!" yazısı
    IEnumerator BigBangText(Vector2 canvasPos, int tier = 1)
    {
        if (_scorePopupLayer == null) yield break;

        var go = new GameObject("BigBangLabel");
        go.transform.SetParent(_scorePopupLayer, false);

        string bangText = tier == 1 ? "BIG BANG!"
                        : tier == 2 ? "MEGA BANG!"
                        :             "ULTRA BANG!";

        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = bangText;
        tmp.fontSize  = 108f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(640f, 180f);
        rt.anchoredPosition = canvasPos;
        rt.localScale       = Vector3.zero;

        // Phase 1 — scale-in ile sıçrama (0→1.30→1.0, 0.22s)
        const float inDur = 0.22f;
        float t = 0f;
        while (t < inDur)
        {
            t += Time.deltaTime;
            float frac = t / inDur;
            float s    = frac < 0.75f
                ? Mathf.Lerp(0f,    1.30f, frac / 0.75f)   // hızlı büyü
                : Mathf.Lerp(1.30f, 1.00f, (frac - 0.75f) / 0.25f); // küçük geri çekme
            rt.localScale = Vector3.one * s;
            // Renk: beyaz → altın
            tmp.color = Color.Lerp(Color.white, new Color(1f, 0.85f, 0.10f), frac);
            yield return null;
        }
        rt.localScale = Vector3.one;
        tmp.color = new Color(1f, 0.85f, 0.10f); // altın

        // Phase 2 — nefes vurması (1.0→1.08→1.0, 0.22s)
        const float pulseDur = 0.22f;
        t = 0f;
        while (t < pulseDur)
        {
            t += Time.deltaTime;
            float frac = t / pulseDur;
            float s    = frac < 0.5f
                ? Mathf.Lerp(1.00f, 1.08f, frac / 0.5f)
                : Mathf.Lerp(1.08f, 1.00f, (frac - 0.5f) / 0.5f);
            rt.localScale = Vector3.one * s;
            yield return null;
        }
        rt.localScale = Vector3.one;

        // Phase 3 — yukarı süzül + kaybol (0.80s)
        const float fadeDur = 0.80f;
        Vector2 startPos = canvasPos;
        Vector2 endPos   = canvasPos + new Vector2(0f, 220f);
        Color   gold     = new Color(1f, 0.85f, 0.10f);
        t = 0f;
        while (t < fadeDur)
        {
            t += Time.deltaTime;
            float frac  = t / fadeDur;
            float ease  = 1f - Mathf.Pow(1f - frac, 2f);
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
            float alpha = frac < 0.45f ? 1f : Mathf.InverseLerp(1f, 0.45f, frac);
            tmp.color = new Color(gold.r, gold.g, gold.b, alpha);
            yield return null;
        }

        Destroy(go);
    }

    // Crossfade the lang pill label: alpha 1→0, swap text, alpha 0→1 (0.15 s total)
    IEnumerator CycleLangAnimated()
    {
        const float half = 0.075f;

        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            if (_langLabel)
                _langLabel.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01(elapsed / half));
            yield return null;
        }
        if (_langLabel) _langLabel.color = new Color(1f, 1f, 1f, 0f);

        LocalizationManager.CycleNext();   // fires OnLanguageChanged → RefreshTexts

        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            if (_langLabel)
                _langLabel.color = new Color(1f, 1f, 1f, Mathf.Clamp01(elapsed / half));
            yield return null;
        }
        if (_langLabel) _langLabel.color = PrimaryText;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static GameObject MakeVerticalGroup(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment        = TextAnchor.MiddleLeft;
        vlg.spacing               = 2f;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        return go;
    }

    static TMP_Text MakeText(Transform parent, string name, string text,
                             float size, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Left;
        return tmp;
    }
}
