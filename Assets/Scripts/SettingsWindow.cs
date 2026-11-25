using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public sealed class SettingsWindow : MonoBehaviour
{
    [SerializeField] private Slider sensitivity, volume;
    [SerializeField] private TMP_Text sensitivityText, volumeText, resolutionText, modeText;
    [SerializeField] private CameraMovementSettings cameraDefaults;

    [Serializable]
    private class Preferences
    {
        public float sensitivity, volume;
        public int width, height;
        public FullScreenMode mode;
    }

    private const string Key = "TrickShot.Settings.1";
    private static SettingsWindow instance;
    private static Preferences applied;
    private static int closedFrame = -1;
    public static bool BlocksInput => (instance && instance.gameObject.activeSelf) || closedFrame == Time.frameCount;
    public static float Sensitivity(float fallback) => applied?.sensitivity ?? fallback;
    private static readonly Vector2Int DefaultSize = new(1920, 1080);
    private static readonly Vector2Int[] sizes = { new(1280, 720), new(1600, 900), DefaultSize };
    private int sizeIndex, modeIndex;
    private CursorLockMode previousLock;
    private bool previousVisible;
    private GameObject previousSelection;
    private static readonly FullScreenMode[] modes = {
        FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed
    };
    private static readonly string[] modeNames = { "Fullscreen", "Borderless", "Windowed" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Restore()
    {
        instance = null;
        closedFrame = -1;
        applied = PlayerPrefs.HasKey(Key) ? JsonUtility.FromJson<Preferences>(PlayerPrefs.GetString(Key)) : null;
        if (applied == null)
        {
            Screen.SetResolution(DefaultSize.x, DefaultSize.y, Screen.fullScreenMode);
            return;
        }
        if (Array.IndexOf(sizes, new Vector2Int(applied.width, applied.height)) < 0)
        {
            applied.width = DefaultSize.x;
            applied.height = DefaultSize.y;
        }
        ApplyToGame(applied);
    }

    private static void ApplyToGame(Preferences values)
    {
        AudioListener.volume = values.volume;
        Screen.SetResolution(values.width, values.height, values.mode);
    }

    public static void Open()
    {
        if (BlocksInput) return;
        if (!instance)
        {
            instance = Instantiate(Resources.Load<SettingsWindow>("SettingsCanvas"));
            DontDestroyOnLoad(instance.gameObject);
        }
        instance.Show();
    }

    private void Awake()
    {
        sensitivity.onValueChanged.AddListener(_ => RefreshLabels());
        volume.onValueChanged.AddListener(_ => RefreshLabels());
        SceneManager.activeSceneChanged += SceneChanged;
    }

    private void OnDestroy() => SceneManager.activeSceneChanged -= SceneChanged;

    private void SceneChanged(Scene oldScene, Scene newScene)
    {
        if (gameObject.activeSelf) Close();
    }

    private void Show()
    {
        previousLock = Cursor.lockState;
        previousVisible = Cursor.visible;
        previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        gameObject.SetActive(true);
        sensitivity.SetValueWithoutNotify(Sensitivity(cameraDefaults.DefaultSensitivity));
        volume.SetValueWithoutNotify(applied?.volume ?? AudioListener.volume);
        var selected = applied == null ? DefaultSize : new Vector2Int(applied.width, applied.height);
        sizeIndex = Array.IndexOf(sizes, selected);
        modeIndex = Array.IndexOf(modes, applied?.mode ?? Screen.fullScreenMode);
        if (modeIndex < 0) modeIndex = 1;
        RefreshLabels();
        sensitivity.Select();
    }

    private void RefreshLabels()
    {
        sensitivityText.text = sensitivity.value.ToString("0%");
        volumeText.text = volume.value.ToString("0%");
        resolutionText.text = $"{sizes[sizeIndex].x} × {sizes[sizeIndex].y}";
        modeText.text = modeNames[modeIndex];
    }

    public void NextResolution() { sizeIndex = (sizeIndex + 1) % sizes.Length; RefreshLabels(); }
    public void NextMode() { modeIndex = (modeIndex + 1) % modes.Length; RefreshLabels(); }

    public void Apply()
    {
        applied = new Preferences { sensitivity = sensitivity.value, volume = volume.value,
            width = sizes[sizeIndex].x, height = sizes[sizeIndex].y, mode = modes[modeIndex] };
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(applied));
        PlayerPrefs.Save();
        ApplyToGame(applied);
        Close();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    public void Close()
    {
        closedFrame = Time.frameCount;
        gameObject.SetActive(false);
        Cursor.lockState = previousLock;
        Cursor.visible = previousVisible;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(previousSelection);
    }
}
