using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RemoteSubtitleStack : MonoBehaviour
{
    public static RemoteSubtitleStack Instance { get; private set; }

    [SerializeField] private int maxLines = 6;
    [SerializeField] private float defaultLifetimeSeconds = 4f;
    [SerializeField] private float fadeSeconds = 0.5f;
    [SerializeField] private int fontSize = 22;

    private readonly List<Entry> entries = new List<Entry>();
    private RectTransform container;

    private struct Entry
    {
        public TMP_Text text;
        public float createdAt;
        public float expiresAt;
    }

    public static void Ensure()
    {
        if (Instance != null) return;
        new GameObject(nameof(RemoteSubtitleStack)).AddComponent<RemoteSubtitleStack>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
    }

    public void Push(string message, float? lifetimeSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (container == null) BuildUI();
        if (container == null) return;

        var lineObject = new GameObject("Subtitle", typeof(RectTransform));
        lineObject.transform.SetParent(container, false);

        var tmp = lineObject.AddComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = fontSize;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.alignment = TextAlignmentOptions.BottomLeft;
        tmp.color = new Color(1f, 1f, 1f, 1f);

        float now = Time.unscaledTime;
        float lifetime = Mathf.Max(0.1f, lifetimeSeconds ?? defaultLifetimeSeconds);
        entries.Add(new Entry
        {
            text = tmp,
            createdAt = now,
            expiresAt = now + lifetime
        });

        Trim();
    }

    private void Update()
    {
        if (entries.Count == 0) return;

        float now = Time.unscaledTime;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry.text == null)
            {
                entries.RemoveAt(i);
                continue;
            }

            float remaining = entry.expiresAt - now;
            if (remaining <= 0f)
            {
                Destroy(entry.text.gameObject);
                entries.RemoveAt(i);
                continue;
            }

            float alpha = 1f;
            if (fadeSeconds > 0f && remaining < fadeSeconds)
            {
                alpha = Mathf.Clamp01(remaining / fadeSeconds);
            }

            var c = entry.text.color;
            c.a = alpha;
            entry.text.color = c;
        }
    }

    private void Trim()
    {
        while (entries.Count > maxLines)
        {
            if (entries[0].text != null)
            {
                Destroy(entries[0].text.gameObject);
            }
            entries.RemoveAt(0);
        }
    }

    private void BuildUI()
    {
        // Build a dedicated overlay canvas to avoid depending on scene wiring.
        var canvasObject = new GameObject("RemoteSubtitlesCanvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        container = new GameObject("Container", typeof(RectTransform)).GetComponent<RectTransform>();
        container.SetParent(canvasObject.transform, false);

        container.anchorMin = new Vector2(0f, 0f);
        container.anchorMax = new Vector2(0f, 0f);
        container.pivot = new Vector2(0f, 0f);
        container.anchoredPosition = new Vector2(16f, 16f);
        container.sizeDelta = new Vector2(520f, 220f);

        var layout = container.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 4f;

        var fitter = container.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }
}
