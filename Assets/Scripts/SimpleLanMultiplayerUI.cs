using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SimpleLanMultiplayerUI : MonoBehaviour
{
    private SimpleLanMultiplayer net;

    private Canvas canvas;
    private Text statusText;
    private InputField addressField;
    private InputField codeField;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
#if UNITY_2023_1_OR_NEWER
        if (FindAnyObjectByType<SimpleLanMultiplayerUI>() != null)
#else
        if (FindObjectOfType<SimpleLanMultiplayerUI>() != null)
#endif
        {
            return;
        }

        GameObject go = new GameObject(nameof(SimpleLanMultiplayerUI));
        DontDestroyOnLoad(go);
        go.AddComponent<SimpleLanMultiplayerUI>();
    }

    private void Start()
    {
#if UNITY_2023_1_OR_NEWER
        net = FindAnyObjectByType<SimpleLanMultiplayer>();
#else
        net = FindObjectOfType<SimpleLanMultiplayer>();
#endif
        EnsureEventSystem();
        BuildUI();
        RefreshFieldsFromNet();
    }

    private void Update()
    {
        if (net == null)
        {
#if UNITY_2023_1_OR_NEWER
            net = FindAnyObjectByType<SimpleLanMultiplayer>();
#else
            net = FindObjectOfType<SimpleLanMultiplayer>();
#endif
            if (net != null)
            {
                RefreshFieldsFromNet();
            }
        }

        if (statusText != null)
        {
            statusText.text = net != null ? net.Status : "No SimpleLanMultiplayer found";
        }
    }

    private void EnsureEventSystem()
    {
#if UNITY_2023_1_OR_NEWER
        if (FindAnyObjectByType<EventSystem>() != null)
#else
        if (FindObjectOfType<EventSystem>() != null)
#endif
        {
            return;
        }

        GameObject es = new GameObject("EventSystem");
        DontDestroyOnLoad(es);
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("LAN_UI_Canvas");
        DontDestroyOnLoad(canvasGO);
        canvasGO.AddComponent<RectTransform>();
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject panelGO = CreateUIObject("Panel", canvas.transform);
        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 0f);
        panelRect.pivot = new Vector2(0f, 0f);
        panelRect.anchoredPosition = new Vector2(10f, 10f);
        panelRect.sizeDelta = new Vector2(320f, 180f);

        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);

        VerticalLayoutGroup layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 6;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        statusText = CreateText("Status", panelGO.transform, "Disconnected");

        addressField = CreateInput("Address", panelGO.transform, "Host IP (e.g. 192.168.1.10)");
        codeField = CreateInput("Code", panelGO.transform, "Room code");

        GameObject rowGO = CreateUIObject("Buttons", panelGO.transform);
        HorizontalLayoutGroup row = rowGO.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 6;
        row.childControlHeight = true;
        row.childControlWidth = true;
        row.childForceExpandWidth = true;

        CreateButton("Host", rowGO.transform, OnHostClicked);
        CreateButton("Join", rowGO.transform, OnJoinClicked);
        CreateButton("Disconnect", rowGO.transform, OnStopClicked);
    }

    private void RefreshFieldsFromNet()
    {
        if (net == null)
        {
            return;
        }

        if (codeField != null && string.IsNullOrEmpty(codeField.text))
        {
            codeField.text = net.SessionCode;
        }
    }

    private void OnHostClicked()
    {
        if (net == null)
        {
            return;
        }

        if (codeField != null)
        {
            net.SetSessionCode(codeField.text);
        }

        net.StartHost();

        if (codeField != null)
        {
            codeField.text = net.SessionCode;
        }
    }

    private void OnJoinClicked()
    {
        if (net == null)
        {
            return;
        }

        if (addressField != null)
        {
            net.SetJoinAddress(addressField.text);
        }

        if (codeField != null)
        {
            net.SetSessionCode(codeField.text);
        }

        string address = addressField != null ? (addressField.text ?? string.Empty).Trim() : string.Empty;

        // If address is empty, SimpleLanMultiplayer will auto-discover a host on LAN.
        net.StartClient(address);
    }

    private void OnStopClicked()
    {
        if (net == null)
        {
            return;
        }

        net.StopSession();
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Text CreateText(string name, Transform parent, string text)
    {
        GameObject go = CreateUIObject(name, parent);
        Text uiText = go.AddComponent<Text>();

        // Unity changed built-in font names; prefer LegacyRuntime.ttf, fallback to Arial.ttf.
        Font font = null;
        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (font == null)
        {
            try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }

        uiText.font = font;
        uiText.fontSize = 14;
        uiText.color = Color.white;
        uiText.text = text;
        uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
        uiText.verticalOverflow = VerticalWrapMode.Overflow;
        return uiText;
    }

    private static InputField CreateInput(string name, Transform parent, string placeholder)
    {
        GameObject root = CreateUIObject(name, parent);
        Image bg = root.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.08f);

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 28f);

        InputField field = root.AddComponent<InputField>();
        field.targetGraphic = bg;

        Text text = CreateText("Text", root.transform, "");
        text.alignment = TextAnchor.MiddleLeft;
        RectTransform textRt = text.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0f, 0f);
        textRt.anchorMax = new Vector2(1f, 1f);
        textRt.offsetMin = new Vector2(8f, 6f);
        textRt.offsetMax = new Vector2(-8f, -6f);

        Text ph = CreateText("Placeholder", root.transform, placeholder);
        ph.alignment = TextAnchor.MiddleLeft;
        ph.color = new Color(1f, 1f, 1f, 0.4f);
        RectTransform phRt = ph.GetComponent<RectTransform>();
        phRt.anchorMin = new Vector2(0f, 0f);
        phRt.anchorMax = new Vector2(1f, 1f);
        phRt.offsetMin = new Vector2(8f, 6f);
        phRt.offsetMax = new Vector2(-8f, -6f);

        field.textComponent = text;
        field.placeholder = ph;

        return field;
    }

    private static void CreateButton(string label, Transform parent, System.Action onClick)
    {
        GameObject go = CreateUIObject(label + "Button", parent);
        Image img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.12f);

        Button button = go.AddComponent<Button>();
        button.onClick.AddListener(() => onClick());

        Text t = CreateText("Label", go.transform, label);
        t.alignment = TextAnchor.MiddleCenter;
        RectTransform tr = t.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 30f);
    }
}
