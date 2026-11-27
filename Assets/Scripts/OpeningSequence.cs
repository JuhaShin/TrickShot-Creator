using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class OpeningSequence : MonoBehaviour
{
    [SerializeField, Min(0f)] private float duration = 1f;
    [SerializeField] private Camera openingCamera;
    [SerializeField] private OpeningWalkHome walkHome;
    [SerializeField] private Canvas openingHud;
    [SerializeField] private CameraMovementSettings movementSettings;
    [SerializeField] private Rigidbody characterBody;
    [SerializeField, Min(0f)] private float interactionDistance = 2f;
    [SerializeField] private Collider posTrigger;
    [SerializeField] private BoxCollider doorBlocker;
    [SerializeField] private Collider doorTrigger;
    [SerializeField] private InteractionPromptUI prompt;
    [SerializeField] private string posPrompt = "Use POS";
    [SerializeField] private string doorPrompt = "Close Restaurant";
    private RaycastHit[] sightHits = new RaycastHit[8];

    public event System.Action QuestProgressChanged;
    public int CompletedObjectives => leaving ? 2 : restaurantClosed ? 1 : 0;

    private bool restaurantClosed;
    private bool leaving;
    private Vector3 velocity;
    private bool cutscenePlaying;
    private bool loading;

    private Collider FindInteraction()
    {
        if (leaving) return null;
        Collider trigger = restaurantClosed ? doorTrigger : posTrigger;
        if (!trigger || !trigger.enabled || !trigger.gameObject.activeInHierarchy) return null;
        Ray ray = openingCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        // The existing triggers fit the models. Test their overlap with the player's reach
        // so the player can interact from outside the solid counter or door.
        if ((trigger.ClosestPoint(ray.origin) - ray.origin).sqrMagnitude >
            interactionDistance * interactionDistance) return null;
        int count;
        while ((count = Physics.RaycastNonAlloc(ray, sightHits, interactionDistance,
                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) == sightHits.Length)
            System.Array.Resize(ref sightHits, sightHits.Length * 2);

        Collider nearest = null;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = sightHits[i];
            Collider target = hit.collider;
            if ((characterBody != null && target.attachedRigidbody == characterBody) ||
                (target.isTrigger && target != posTrigger) || hit.distance >= distance) continue;
            nearest = target;
            distance = hit.distance;
        }
        return nearest == (restaurantClosed ? doorBlocker : posTrigger) ? nearest : null;
    }

    private void Interact(Collider nearest)
    {
        if (nearest == posTrigger && !restaurantClosed)
        {
            restaurantClosed = true;
            QuestProgressChanged?.Invoke();
            Debug.Log("Restaurant closed.", this);
        }
        else if (restaurantClosed && nearest == doorBlocker && doorTrigger != null)
        {
            leaving = true;
            QuestProgressChanged?.Invoke();
            StartCoroutine(OpenDoorAndLeave());
        }
    }

    private IEnumerator OpenDoorAndLeave()
    {
        SetCutscenePlaying(true);
        Transform blocker = doorBlocker.transform;
        Vector3 threshold = blocker.TransformPoint(doorBlocker.center) -
            Vector3.up * doorBlocker.bounds.extents.y;
        Vector3 outward = blocker.right;
        Vector3 hinge = blocker.TransformPoint(doorBlocker.center +
            Vector3.forward * (doorBlocker.size.z * .5f));
        Vector3 axis = blocker.up;
        Transform leaf = doorTrigger.transform;
        Transform handle = leaf.parent.Find("Door Pull");
        float elapsed = 0f;
        float previousAngle = 0f;
        while (elapsed < .4f)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
            float angle = Mathf.SmoothStep(0f, -90f, elapsed / .4f);
            if (angle == previousAngle) continue;
            float delta = angle - previousAngle;
            blocker.RotateAround(hinge, axis, delta);
            leaf.RotateAround(hinge, axis, delta);
            if (handle != null && !handle.IsChildOf(leaf)) handle.RotateAround(hinge, axis, delta);
            previousAngle = angle;
        }

        if (openingHud) openingHud.enabled = false;
        yield return walkHome.WaitForDeparture(duration);
        yield return walkHome.Play(openingCamera, threshold, outward);
        CompleteOpening();
    }
    public void CompleteOpening()
    {
        if (loading || !ProgressSave.Enter("MainScene")) return;
        loading = true;
        UpdateCursor();
        SceneManager.LoadSceneAsync("MainScene");
    }

    public void SetCutscenePlaying(bool playing)
    {
        cutscenePlaying = playing;
        if (playing) velocity = Vector3.zero;
        UpdateCursor();
    }

    private void OnEnable() => UpdateCursor();
    private void OnApplicationFocus(bool focused) => UpdateCursor();

    private void UpdateCursor()
    {
        if (!isActiveAndEnabled) return;
        bool locked = Application.isFocused && !cutscenePlaying && !loading;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnDisable()
    {
        if (prompt) prompt.Show(null);
        if (loading) return; // Control has already been released before the scene load.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        if (cutscenePlaying || loading || SettingsWindow.BlocksInput || !Application.isFocused || openingCamera == null)
        {
            prompt.Show(null);
            velocity = Vector3.zero;
            return;
        }
        Transform view = openingCamera.transform;
        Vector2 delta = movementSettings.ReadLookDelta();
        if (delta != Vector2.zero)
        {
            Vector3 angles = view.eulerAngles;
            float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x) - delta.y, -89f, 89f);
            view.rotation = Quaternion.Euler(pitch, angles.y + delta.x, 0f);
        }
        Collider target = FindInteraction();
        prompt.Show(target == null ? null : target == posTrigger ? posPrompt : doorPrompt);
        if (target != null && Input.GetKeyDown(KeyCode.E))
        {
            Interact(target);
            prompt.Show(null);
        }
        Vector3 input = movementSettings.ReadMoveInput();
        velocity = movementSettings.UpdateVelocity(velocity, input, view.eulerAngles.y, Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (characterBody == null || characterBody.isKinematic) return;
        if (cutscenePlaying || loading || !Application.isFocused) velocity = Vector3.zero;
        Vector3 motion = characterBody.linearVelocity;
        motion.x = velocity.x;
        motion.z = velocity.z;
        characterBody.linearVelocity = motion;
    }
}