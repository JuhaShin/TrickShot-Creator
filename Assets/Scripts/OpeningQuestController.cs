using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class OpeningQuestController : MonoBehaviour
{
    [SerializeField] private OpeningSequence sequence;
    [SerializeField] private QuestHighlight highlight;
    [SerializeField] private Transform posModel;
    [SerializeField] private GameObject doorLeaf;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite headerSprite;
    [SerializeField] private Sprite bodySprite;
    [SerializeField] private Sprite uncheckedSprite;
    [SerializeField] private Sprite checkedSprite;
    private static readonly Color32 Ink = new Color32(37, 54, 74, 255);
    private static readonly Color32 Blue = new Color32(67, 188, 235, 255);
    private static readonly Color32 Pending = new Color32(205, 214, 223, 255);
    private static readonly string[] Objectives = { "Close the restaurant.", "Go home." };
    private readonly Image[] boxes = new Image[Objectives.Length];
    private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[Objectives.Length];
    private readonly RectTransform[] completionLines = new RectTransform[Objectives.Length];
    private RectTransform panel;
    private TextMeshProUGUI count;
    private Image fill;

    private void Awake()
    {
        Sprite placeholder = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        panel = Node("QuestPanel", transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 1);
        panel.anchoredPosition = new Vector2(30, -30);
        panel.sizeDelta = new Vector2(420, 0);
        Column(panel);
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform header = Node("Header", panel);
        Background(header, headerSprite ? headerSprite : placeholder, headerSprite ? Color.white : new Color32(255, 139, 50, 255));
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
        Row(header, 14);
        Label("Title", header, "Quest", true).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        count = Label("ProgressText", header, "", true);

        RectTransform body = Node("Body", panel);
        Background(body, bodySprite ? bodySprite : placeholder, Color.white);
        VerticalLayoutGroup bodyLayout = Column(body);
        bodyLayout.padding = new RectOffset(14, 14, 8, 12);
        bodyLayout.spacing = 8;
        RectTransform container = Node("ObjectiveContainer", body);
        Column(container);
        for (int i = 0; i < Objectives.Length; i++)
        {
            RectTransform row = Node("Objective", container);
            Row(row, 0);
            row.gameObject.AddComponent<LayoutElement>().minHeight = 36;
            RectTransform box = Node("CheckBox", row);
            boxes[i] = Background(box, uncheckedSprite, Color.white);
            boxes[i].type = Image.Type.Simple;
            boxes[i].preserveAspect = true;
            LayoutElement size = box.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = size.preferredHeight = 18;
            labels[i] = Label("ObjectiveText", row, Objectives[i], false);
            labels[i].gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var line = Node("CompletionLine", labels[i].transform);
            line.anchorMin = line.anchorMax = labels[i].rectTransform.pivot;
            var image = line.gameObject.AddComponent<Image>();
            image.color = Ink;
            image.raycastTarget = false;
            labels[i].OnPreRenderText += info => AlignCompletionLine(line, info);
            completionLines[i] = line;
        }
        RectTransform track = Node("ProgressBar", body);
        Background(track, placeholder, Pending);
        track.gameObject.AddComponent<LayoutElement>().preferredHeight = 6;
        RectTransform bar = Node("Fill", track);
        bar.anchorMin = Vector2.zero;
        bar.anchorMax = Vector2.one;
        bar.offsetMin = bar.offsetMax = Vector2.zero;
        fill = Background(bar, placeholder, Blue);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
    }

    private void OnEnable()
    {
        sequence.QuestProgressChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        sequence.QuestProgressChanged -= Refresh;
        highlight.SetTarget(null);
    }

    private void Refresh()
    {
        int done = sequence.CompletedObjectives;
        highlight.SetTarget(done == 0 ? posModel.gameObject : done == 1 ? doorLeaf : null);
        for (int i = 0; i < Objectives.Length; i++)
        {
            boxes[i].sprite = i < done ? checkedSprite : uncheckedSprite;
            completionLines[i].gameObject.SetActive(i < done);
        }
        count.text = $"{done} / {Objectives.Length}";
        fill.fillAmount = (float)done / Objectives.Length;
        panel.gameObject.SetActive(done < Objectives.Length);
    }

    private static void AlignCompletionLine(RectTransform line, TMP_TextInfo info)
    {
        Vector2 min = Vector2.positiveInfinity;
        Vector2 max = Vector2.negativeInfinity;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo glyph = info.characterInfo[i];
            if (!glyph.isVisible) continue;
            min = Vector2.Min(min, glyph.bottomLeft);
            max = Vector2.Max(max, glyph.topRight);
        }
        if (float.IsInfinity(min.x))
        {
            line.sizeDelta = Vector2.zero;
            return;
        }
        line.anchoredPosition = (min + max) * .5f;
        line.sizeDelta = new Vector2(max.x - min.x, 2f);
    }

    private static RectTransform Node(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image Background(RectTransform rect, Sprite sprite, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TextMeshProUGUI Label(string name, Transform parent, string value, bool bold)
    {
        var text = Node(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = 22;
        text.color = Ink;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.text = value;
        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        return text;
    }

    private static VerticalLayoutGroup Column(RectTransform rect)
    {
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    private static void Row(RectTransform rect, int padding)
    {
        var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, 0, 0);
        layout.spacing = 10;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = layout.childForceExpandWidth = false;
    }
}
