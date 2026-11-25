using UnityEngine;
using UnityEngine.SceneManagement;

public class MainSceneGameManager : MonoBehaviour
{
    public const string FryingPanKey = "FryingPan";
    public const string PotKey = "Pot";
    // 배열 인덱스 0/1 = 슬롯 1/2. 키 → 아이콘 / 프리팹 / 남은 수량
    // FryingPan → FryingPanSprite / fryingPanPrefab / RemainingFryingPans
    // Pot      → PotSprite       / potPrefab       / RemainingPots
    [Tooltip("슬롯 1, 2 순서. FryingPan = 프라이팬, Pot = 냄비. 각 키를 한 번씩 입력하세요.")]
    [SerializeField] private string[] toolOrder = { FryingPanKey, PotKey };
    private static string[] pendingToolOrder;

    public const int MaxSimilarVideos = 9;
    [SerializeField, Min(0)] private long initialSubscribers;
    [SerializeField, Min(1)] private double growthScale = 1000000;
    [SerializeField, Range(1, 5)] private int computerTier = 1;
    [SerializeField, Min(0.001f)] private double cameraMultiplier = 1;
    [SerializeField, Min(0.001f)] private double microphoneMultiplier = 1;

    private static readonly (float min, float max)[] pcRanges =
        { (1.01f, 1.05f), (1.03f, 1.10f), (1.05f, 1.20f), (1.10f, 1.30f), (1.20f, 1.50f) };
    private static readonly (double views, double conversion)[] penalties =
        { (1, 1), (.85, .80), (.65, .60), (.40, .40), (.20, .20) };
    private static (int objects, double seconds, int types, int bounces, int similar)? pendingUpload;
    [SerializeField, Min(0)] private long initialViews;
    [SerializeField] private TMPro.TextMeshProUGUI totalSubscribersText;
    [SerializeField] private TMPro.TextMeshProUGUI totalViewsText;

    [SerializeField] private TMPro.TextMeshProUGUI dayText;
    [SerializeField] private TMPro.TextMeshProUGUI resultText;
    private static int currentDay = 1;
    private const int FirstViralDay = 3;
    private static int lastUploadDay;
    private bool presenting;
    private static bool initialized;
    public static long TotalSubscribers { get; private set; }
    public static long TotalViews { get; private set; }
    public static long LastSubscriberChange { get; private set; }
    public static long LastViewChange { get; private set; }
    public static (long before, double score, double pc, double camera, double mic,
        long originalViews, double expansion, int similar, double viewRate, double conversionRate,
        long views, long gained, long total)? LastResult { get; private set; }

    internal static void QueueUpload(int objects, double seconds, int types, int bounces, int similar)
    {
        pendingUpload = (objects, seconds, types, bounces, similar);
        var data = ProgressSave.Data;
        if (data == null) return;
        data.pendingId = System.Guid.NewGuid().ToString("N");
        data.objects = objects; data.seconds = seconds; data.types = types;
        data.bounces = bounces; data.similar = similar;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetGrowth()
    {
        pendingUpload = null;
        pendingToolOrder = null;
        initialized = false;
        currentDay = 1;
        lastUploadDay = 0;
        TotalSubscribers = TotalViews = 0;
        LastSubscriberChange = LastViewChange = 0;
        LastResult = null;
    }

    internal static void RestoreProgress(ProgressSaveData data)
    {
        ResetGrowth();
        initialized = true;
        currentDay = data.day;
        lastUploadDay = data.lastDay;
        TotalSubscribers = data.subscribers;
        TotalViews = data.views;
        LastSubscriberChange = data.newSubscribers;
        LastViewChange = data.newViews;
        pendingToolOrder = data.toolOrder;
        if (data.hasScore)
            LastResult = (data.scoreBefore, data.score, data.pc, data.camera, data.mic, data.originalViews,
                data.expansion, data.scoreSimilar, data.viewRate, data.conversionRate, data.newViews,
                data.newSubscribers, data.subscribers);
        if (!string.IsNullOrEmpty(data.pendingId) && data.pendingId != data.settledId)
            pendingUpload = (data.objects, data.seconds, data.types, data.bounces, data.similar);
    }

    private void Start()
    {
        if (!initialized)
        {
            TotalSubscribers = System.Math.Max(0L, initialSubscribers);
            TotalViews = System.Math.Max(0L, initialViews);
            initialized = true;
        }
        bool uploaded = pendingUpload.HasValue;
        SettleUpload();
        var data = ProgressSave.Data;
        if (uploaded && data != null)
        {
            data.day = currentDay; data.lastDay = lastUploadDay;
            data.subscribers = TotalSubscribers; data.views = TotalViews;
            data.newSubscribers = LastSubscriberChange; data.newViews = LastViewChange;
            data.settledId = data.pendingId; data.pendingId = "";
            if (LastResult.HasValue)
            {
                var result = LastResult.Value;
                data.hasScore = true; data.scoreBefore = result.before; data.score = result.score;
                data.pc = result.pc; data.camera = result.camera; data.mic = result.mic;
                data.originalViews = result.originalViews; data.expansion = result.expansion;
                data.scoreSimilar = result.similar; data.viewRate = result.viewRate;
                data.conversionRate = result.conversionRate;
            }
            data.unread = true;
            if (!ProgressSave.Write(data))
            {
                presenting = true;
                StartCoroutine(RetrySettlement(data));
                return;
            }
        }
        if (uploaded || (data != null && data.unread)) StartCoroutine(PresentDayChange());
        else ShowTotals();
    }

    private System.Collections.IEnumerator RetrySettlement(ProgressSaveData data)
    {
        do { yield return new WaitForSecondsRealtime(2f); } while (!ProgressSave.Write(data));
        yield return PresentDayChange();
    }

    private void ShowTotals()
    {
        dayText.text = $"Day {currentDay}";
        if (lastUploadDay > 0)
            resultText.text = $"Day {lastUploadDay}   +{LastSubscriberChange:N0} subs" +
                (lastUploadDay >= FirstViralDay ? $"   +{LastViewChange:N0} views" : "");
        totalSubscribersText.text = TotalSubscribers.ToString("N0");
        totalViewsText.text = TotalViews.ToString("N0");
    }

    private void SettleUpload()
    {
        if (!pendingUpload.HasValue) return;
        lastUploadDay = currentDay;
        if (lastUploadDay < FirstViralDay)
        {
            int gained = lastUploadDay == 2 ? Random.Range(50, 501)
                : Mathf.Max(0, lastUploadDay * lastUploadDay - Random.Range(0, 10));
            ApplyGrowth(gained, 0);
        }
        else if (lastUploadDay == FirstViralDay)
        {
            long gained = Random.Range(1000, 5000);
            ApplyGrowth(gained, gained * Random.Range(3, 10));
        }
        else
        {
            SettleRegularUpload();
        }
        pendingUpload = null;
        currentDay++;
    }

    private void SettleRegularUpload()
    {
        var shot = pendingUpload.Value;
        long before = TotalSubscribers;
        double objects = shot.objects == 0 ? 0 : (.25 + 2 * System.Math.Log10(System.Math.Min(10, shot.objects))) / 2.25;
        double time = (1 - System.Math.Pow(.97, System.Math.Min(30, shot.seconds))) / (1 - System.Math.Pow(.97, 30));
        double diversity = System.Math.Clamp((shot.types - 1) / 4.0, 0, 1);
        double bounce = System.Math.Log(1.0 + shot.bounces) / System.Math.Log(51.0 + shot.bounces);
        double score = System.Math.Clamp(1 + .05 * objects + .45 * time + .20 * diversity + .80 * bounce, 1, 2.5);
        var range = pcRanges[Mathf.Clamp(computerTier, 1, 5) - 1];
        double pc = Random.Range(range.min, range.max);
        double q = pc * score * cameraMultiplier;
        double expansion = before / (1 + before / System.Math.Max(1, growthScale)) * System.Math.Max(0, q - 1);
        long original = FloorCount(before * System.Math.Min(1, q) + expansion);
        var rate = penalties[Mathf.Clamp(shot.similar, 0, penalties.Length - 1)];
        long views = FloorCount(original * rate.views);
        double channelGrowthRate = System.Math.Clamp(
            1 - .2375 * System.Math.Log10(System.Math.Max(1000L, before) / 1000.0), .05, 1);
        // A standard shot earns 95% of the target growth. Quality and equipment bring it toward 100%.
        double performance = q * score * microphoneMultiplier;
        double performanceFactor = performance <= 1 ? .95 * performance : 1 - .05 / performance;
        long gained = FloorCount(before * channelGrowthRate * performanceFactor * rate.views * rate.conversion);
        gained = System.Math.Min(gained, long.MaxValue - before);
        long total = before + gained;
        LastResult = (before, score, pc, cameraMultiplier, microphoneMultiplier, original, expansion,
            shot.similar, rate.views, rate.conversion, views, gained, total);
        ApplyGrowth(gained, views);
    }

    private static void ApplyGrowth(long gained, long views)
    {
        LastSubscriberChange = System.Math.Min(gained, long.MaxValue - TotalSubscribers);
        LastViewChange = System.Math.Min(views, long.MaxValue - TotalViews);
        TotalSubscribers += LastSubscriberChange;
        TotalViews += LastViewChange;
    }

    private System.Collections.IEnumerator PresentDayChange()
    {
        presenting = true;
        var overlay = new GameObject("Day result", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        overlay.transform.SetParent(dayText.canvas.rootCanvas.transform, false);
        var rect = (RectTransform)overlay.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var background = overlay.GetComponent<UnityEngine.UI.Image>();
        background.color = new Color(.07f, .07f, .1f);
        var title = Instantiate(dayText, rect);
        title.rectTransform.anchorMin = Vector2.zero;
        title.rectTransform.anchorMax = Vector2.one;
        title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
        title.alignment = TMPro.TextAlignmentOptions.Center;
        title.text = $"Day {lastUploadDay} → Day {currentDay}";
        var fade = overlay.AddComponent<CanvasGroup>();
        for (float elapsed = 0; elapsed < .35f; elapsed += Time.unscaledDeltaTime)
        {
            fade.alpha = Mathf.Clamp01(elapsed / .35f);
            yield return null;
        }
        fade.alpha = 1;
        yield return new WaitForSecondsRealtime(.45f);
        ShowTotals();
        if (lastUploadDay == FirstViralDay)
        {
            title.text = $"First viral video!\nDay {lastUploadDay}\n+{LastSubscriberChange:N0} subscribers\n{LastViewChange:N0} views\n\nContinue";
            var button = overlay.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = background;
            bool dismissed = false;
            button.onClick.AddListener(() => dismissed = true);
            while (!dismissed) yield return null;
        }
        if (ProgressSave.Data != null)
        {
            ProgressSave.Data.unread = false;
            ProgressSave.Write(ProgressSave.Data);
        }
        Destroy(overlay);
        presenting = false;
    }

    private static long FloorCount(double value) =>
        value >= long.MaxValue ? long.MaxValue : (long)System.Math.Floor(value);

    public void OpenSettings() { if (!presenting) SettingsWindow.Open(); }

    public void LoadStudioScene()
    {
        if (presenting || SettingsWindow.BlocksInput) return;
        LastSubscriberChange = LastViewChange = 0;
        pendingToolOrder = toolOrder == null ? null : (string[])toolOrder.Clone();
        if (ProgressSave.Data != null)
        {
            ProgressSave.Data.draft = ProgressSave.Data.uploaded;
            ProgressSave.Data.toolOrder = pendingToolOrder;
            if (!ProgressSave.Enter("StudioScene")) return;
        }
        SceneManager.LoadScene("StudioScene");
    }

    internal static string[] TakeToolOrder()
    {
        string[] order = pendingToolOrder;
        pendingToolOrder = null;
        if (order != null && order.Length == 2 &&
            ((order[0] == FryingPanKey && order[1] == PotKey) ||
             (order[0] == PotKey && order[1] == FryingPanKey))) return order;
        if (order != null) Debug.LogWarning("Tool Order에는 FryingPan과 Pot을 한 번씩 입력하세요. 기본 순서를 적용합니다.");
        return new[] { FryingPanKey, PotKey };
    }
}
