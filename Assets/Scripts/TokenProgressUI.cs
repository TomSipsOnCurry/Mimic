using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TokenProgressUI : MonoBehaviour
{
    [SerializeField] private Vector2 slotSize = new Vector2(26f, 26f);
    [SerializeField] private float slotSpacing = 5f;
    [SerializeField] private Vector2 panelPadding = new Vector2(10f, 8f);
    [SerializeField] private Vector2 screenPadding = new Vector2(14f, 14f);
    [SerializeField] private float labelHeight = 22f;

    private Image[] slots;
    private TextMeshProUGUI countLabel;

    private static readonly Color FilledColor  = new Color(1f, 0.88f, 0.1f, 1f);
    private static readonly Color EmptyColor   = new Color(0.18f, 0.18f, 0.22f, 1f);
    private static readonly Color BorderColor  = new Color(1f, 1f, 1f, 0.95f);
    private static readonly Color PanelColor   = new Color(0f, 0f, 0f, 0.82f);

    private void Awake()
    {
        BuildUI();
        Refresh(GameManager.GetGlobalTokenCount());
    }

    private void OnEnable()  => GameManager.OnTokensChanged += Refresh;
    private void OnDisable() => GameManager.OnTokensChanged -= Refresh;

    private void BuildUI()
    {
        // Always own a dedicated root canvas so nothing can cover it
        var canvasGO = new GameObject("TokenProgressCanvas");
        DontDestroyOnLoad(canvasGO);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Host panel
        var panelGO = new GameObject("TokenPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        int total = GameManager.TotalTokens;
        float panelW = total * slotSize.x + (total - 1) * slotSpacing + panelPadding.x * 2f;
        float panelH = slotSize.y + labelHeight + panelPadding.y * 2f + 4f;

        // Panel RectTransform — top-right anchor
        var panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(1f, 1f);
        panelRT.anchorMax = new Vector2(1f, 1f);
        panelRT.pivot     = new Vector2(1f, 1f);
        panelRT.anchoredPosition = new Vector2(-screenPadding.x, -screenPadding.y);
        panelRT.sizeDelta = new Vector2(panelW, panelH);

        // White border (extends 3px outside panel)
        var borderGO = new GameObject("Border");
        borderGO.transform.SetParent(panelGO.transform, false);
        var brt = borderGO.AddComponent<RectTransform>();
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(-3f, -3f); brt.offsetMax = new Vector2(3f, 3f);
        borderGO.AddComponent<Image>().color = BorderColor;

        // Dark background
        panelGO.AddComponent<Image>().color = PanelColor;

        // Label "TOKENS  0 / 7"
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(panelGO.transform, false);
        var lrt = labelGO.AddComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f);
        lrt.pivot     = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -panelPadding.y);
        lrt.sizeDelta = new Vector2(0f, labelHeight);

        countLabel = labelGO.AddComponent<TextMeshProUGUI>();
        countLabel.text = $"0 / {total}";
        countLabel.fontSize = 16f;
        countLabel.fontStyle = FontStyles.Bold;
        countLabel.alignment = TextAlignmentOptions.Center;
        countLabel.color = Color.white;
        countLabel.outlineWidth = 0.25f;
        countLabel.outlineColor = Color.black;

        // Slot icons
        slots = new Image[total];
        float slotsTop = panelPadding.y + labelHeight + 4f;

        for (int i = 0; i < total; i++)
        {
            // Slot border
            var borderSlot = new GameObject($"SlotBorder_{i}");
            borderSlot.transform.SetParent(panelGO.transform, false);
            var bsrt = borderSlot.AddComponent<RectTransform>();
            bsrt.anchorMin = new Vector2(0f, 0f); bsrt.anchorMax = new Vector2(0f, 0f);
            bsrt.pivot = new Vector2(0f, 0f);
            float xOff = panelPadding.x + i * (slotSize.x + slotSpacing);
            bsrt.anchoredPosition = new Vector2(xOff - 2f, panelPadding.y - 2f);
            bsrt.sizeDelta = slotSize + new Vector2(4f, 4f);
            borderSlot.AddComponent<Image>().color = BorderColor;

            // Slot fill
            var slotGO = new GameObject($"Slot_{i}");
            slotGO.transform.SetParent(panelGO.transform, false);
            var srt = slotGO.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(0f, 0f);
            srt.pivot = new Vector2(0f, 0f);
            srt.anchoredPosition = new Vector2(xOff, panelPadding.y);
            srt.sizeDelta = slotSize;
            slots[i] = slotGO.AddComponent<Image>();
            slots[i].color = EmptyColor;
        }
    }

    private void Refresh(int count)
    {
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null)
                slots[i].color = i < count ? FilledColor : EmptyColor;

        if (countLabel != null)
            countLabel.text = $"{count} / {GameManager.TotalTokens}";
    }
}
