using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class StartMenu : MonoBehaviour
{
    [SerializeField] private Button continueButton, newGameButton;
    private bool loading;
    private GameObject dialog;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        continueButton.interactable = ProgressSave.Read() != null;
        continueButton.onClick.AddListener(ContinueGame);
    }

    public void OpenSettings()
    {
        if (!loading && !dialog) SettingsWindow.Open();
    }

    public void ContinueGame()
    {
        if (loading || dialog || SettingsWindow.BlocksInput) return;
        var data = ProgressSave.Read();
        if (data == null) { continueButton.interactable = false; return; }
        ProgressSave.Resume(data);
        Launch(data);
    }

    public void NewGame()
    {
        if (loading || dialog || SettingsWindow.BlocksInput) return;
        if (!ProgressSave.Exists) { StartFresh(); return; }
        dialog = new GameObject("Confirm New Game", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)dialog.transform;
        rect.SetParent(newGameButton.GetComponentInParent<Canvas>().transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        dialog.GetComponent<Image>().color = new Color(.07f,.07f,.1f,.99f);
        var message = Instantiate(newGameButton.GetComponentInChildren<TextMeshProUGUI>(), rect);
        message.rectTransform.anchorMin = message.rectTransform.anchorMax = new Vector2(.5f,.5f);
        message.rectTransform.sizeDelta = new Vector2(760,150);
        message.rectTransform.anchoredPosition = new Vector2(0,80);
        message.text = "Start a new game?\nAll saved game progress will be lost.\nYour settings will be kept.";
        var cancel = DialogButton("Cancel",-135,Cancel);
        var accept = DialogButton("Start New Game",135,StartFresh);
        foreach (var b in new[]{cancel,accept})
        {
            var nav = b.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnLeft = nav.selectOnRight = b == cancel ? accept : cancel;
            b.navigation = nav;
        }
        cancel.Select();
    }

    private Button DialogButton(string label,float x,UnityEngine.Events.UnityAction action)
    {
        var button = Instantiate(newGameButton,dialog.transform);
        button.name = label;
        var rect = (RectTransform)button.transform;
        rect.anchoredPosition = new Vector2(x,-65);
        rect.sizeDelta = new Vector2(245,62);
        button.GetComponentInChildren<TextMeshProUGUI>().text = label;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
        return button;
    }

    private void Update()
    {
        if (dialog && !loading && Input.GetKeyDown(KeyCode.Escape)) Cancel();
    }

    private void Cancel()
    {
        dialog.SetActive(false);
        Destroy(dialog);
        dialog = null;
        newGameButton.Select();
    }

    private void StartFresh()
    {
        if (loading) return;
        var data = new ProgressSaveData();
        if (ProgressSave.Write(data,true)) Launch(data);
    }

    private void Launch(ProgressSaveData data)
    {
        loading = true;
        MainSceneGameManager.RestoreProgress(data);
        GameManager.ResetFieldHistory();
        ShotSegmentHistory.ResetSession();
        if (data.segments != null) ShotSegmentHistory.Save(data.segments);
        SceneManager.LoadSceneAsync(data.scene);
    }
}
