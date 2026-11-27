using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class SimulationCameraMovement : MonoBehaviour
{
    [SerializeField] private CameraMovementSettings movementSettings;
    [SerializeField] private bool enableEyeHeightInput;
    private float eyeHeight;
    [SerializeField, Range(1f, 5f)] private float maxZoom = 5f;
    private Transform ball;
    private Rigidbody player;
    private CapsuleCollider capsule;
    private Vector3 moveVelocity;
    private Pose launchView;
    private Camera trackingCamera;
    private float initialFieldOfView, initialHalfFovTangent, launchViewDistance;
    private bool exploring, tracking;
    public Vector3 PlayerPosition => player.position;

    public void RestoreUploadedPosition(Vector3 feet, Pose view)
    {
        PreparePositionSelection();
        player.position = feet;
        player.transform.position = feet;
        transform.SetPositionAndRotation(view.position, view.rotation);
    }

    public void PreparePositionSelection()
    {
        if (trackingCamera == null)
        {
            player = GetComponentInParent<Rigidbody>();
            capsule = player.GetComponent<CapsuleCollider>();
            trackingCamera = GetComponent<Camera>();
            initialFieldOfView = trackingCamera.fieldOfView;
            initialHalfFovTangent = Mathf.Tan(initialFieldOfView * Mathf.Deg2Rad * 0.5f);
        }
        tracking = false;
        trackingCamera.fieldOfView = initialFieldOfView;
    }

    public void StartExploring()
    {
        exploring = true;
        if (enableEyeHeightInput) SetEyeHeight(transform.position.y);
        capsule.enabled = true;
        player.isKinematic = false;
        SetExploreCursor(Application.isFocused);
    }

    private static void SetExploreCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (exploring) SetExploreCursor(focused);
    }

    public void MoveBeforeLaunch(Bounds area)
    {
        if (!Application.isFocused) { moveVelocity = Vector3.zero; return; }
        Vector2 look = movementSettings.ReadLookDelta();
        Vector3 angles = transform.eulerAngles;
        if (enableEyeHeightInput)
        {
            var keyboard = Keyboard.current;
            float vertical = keyboard == null ? 0f : keyboard.spaceKey.ReadValue() - keyboard.shiftKey.ReadValue();
            SetEyeHeight(eyeHeight + vertical * Time.deltaTime);
        }
        float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x) - look.y, -89f, 89f);
        transform.rotation = Quaternion.Euler(pitch, angles.y + look.x, 0f);
        moveVelocity = movementSettings.UpdateVelocity(moveVelocity, movementSettings.ReadMoveInput(),
            transform.eulerAngles.y, Time.deltaTime);
        Vector3 p = player.position;
        if ((p.x <= area.min.x && moveVelocity.x < 0) || (p.x >= area.max.x && moveVelocity.x > 0)) moveVelocity.x = 0;
        if ((p.z <= area.min.z && moveVelocity.z < 0) || (p.z >= area.max.z && moveVelocity.z > 0)) moveVelocity.z = 0;
    }

    private void FixedUpdate()
    {
        if (exploring) player.linearVelocity = new Vector3(moveVelocity.x, player.linearVelocity.y, moveVelocity.z);
    }

    public void StopExploring()
    {
        if (!exploring) return;
        exploring = false;
        moveVelocity = Vector3.zero;
        player.linearVelocity = Vector3.zero;
        player.isKinematic = true;
        capsule.enabled = false;
        SetExploreCursor(false);
    }

    public void PrepareLaunchView(Transform target)
    {
        launchView = new Pose(transform.position, transform.rotation);
        StopExploring();
        ball = target;
        launchViewDistance = Vector3.Distance(launchView.position, ball.position);
        RestoreLaunchView();
    }

    public void RestoreLaunchView()
    {
        tracking = false;
        transform.SetPositionAndRotation(launchView.position, launchView.rotation);
        trackingCamera.fieldOfView = initialFieldOfView;
    }

    public void TrackLaunch() => tracking = true;

    private void LateUpdate()
    {
        if (exploring && enableEyeHeightInput) SetEyeHeight(eyeHeight);
        if (tracking) LookAtBall();
    }

    private void SetEyeHeight(float height)
    {
        eyeHeight = Mathf.Clamp(height, 1f, 1.6f);
        Vector3 position = transform.position;
        position.y = eyeHeight;
        transform.position = position;
    }

    public void LookAtBall()
    {
        if (ball == null)
            return;

        // 공 위치에서 카메라 위치를 빼면 카메라에서 공으로 향하는 방향이 된다.
        Vector3 direction = ball.position - transform.position;
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        float distance = direction.magnitude;
        if (distance <= launchViewDistance)
        {
            trackingCamera.fieldOfView = initialFieldOfView;
            return;
        }

        // 발사 구도보다 카메라에서 3유닛 멀어질 때마다 1배씩 확대한다.
        float zoom = Mathf.Min(1f + (distance - launchViewDistance) / 3f, maxZoom);
        trackingCamera.fieldOfView = 2f * Mathf.Atan(initialHalfFovTangent / zoom) * Mathf.Rad2Deg;
    }
}
