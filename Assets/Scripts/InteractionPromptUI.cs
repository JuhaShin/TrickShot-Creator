using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset font;
    private CanvasGroup group;
    private TMP_Text actionText;
    private bool visible;

    private void Awake()
    {
        RectTransform panel = Rect("InteractionPrompt", transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, 0);
        panel.anchoredPosition = new Vector2(0, 36);
        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0, 0, 0, .5f);
        background.raycastTarget = false;
        var layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 8, 8);
        layout.spacing = 10;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform key = Rect("Key", panel);
        key.gameObject.AddComponent<Image>().raycastTarget = false;
        var size = key.gameObject.AddComponent<LayoutElement>();
        size.preferredWidth = size.preferredHeight = 28;
        var keyText = Label(key, "E", Color.black);
        keyText.rectTransform.anchorMin = Vector2.zero;
        keyText.rectTransform.anchorMax = Vector2.one;
        keyText.rectTransform.offsetMin = keyText.rectTransform.offsetMax = Vector2.zero;
        actionText = Label(panel, "", Color.white);
        group = panel.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        group.alpha = 0;
        panel.gameObject.SetActive(false);
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private TMP_Text Label(Transform parent, string value, Color color)
    {
        var label = Rect("Label", parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = 20;
        label.color = color;
        label.text = value;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    public void Show(string message)
    {
        visible = !string.IsNullOrEmpty(message);
        if (!visible) return;
        if (actionText.text != message) actionText.text = message;
        if (!group.gameObject.activeSelf) group.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!group.gameObject.activeSelf) return;
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f,
            Time.unscaledDeltaTime / (visible ? .15f : .1f));
        if (!visible && group.alpha == 0f) group.gameObject.SetActive(false);
    }
}
