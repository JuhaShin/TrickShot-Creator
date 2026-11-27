using UnityEngine;
using UnityEngine.InputSystem;

public class BuildCameraMovement : MonoBehaviour
{
    [SerializeField] private CameraMovementSettings movementSettings;
    [SerializeField] private SphereCollider cameraBody;
    private const float CollisionSkin = 0.002f;
    private Vector3 velocity;
    private float yaw;
    private float pitch;

    private void OnEnable()
    {
        // 카메라가 씬에서 바라보던 방향부터 회전을 시작한다.
        velocity = Vector3.zero;
        cameraBody.enabled = true;
        ApplyMovementLimits(transform.position);
        yaw = transform.eulerAngles.y;
        pitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x);
        SetCursorLocked(Application.isFocused);
    }

    private void OnDisable()
    {
        cameraBody.enabled = false;
        SetCursorLocked(false);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (isActiveAndEnabled && !SettingsWindow.BlocksInput) SetCursorLocked(focused);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void Update()
    {
        if (SettingsWindow.BlocksInput) { velocity = Vector3.zero; return; }
        Vector2 delta = movementSettings.ReadLookDelta();
        if (delta != Vector2.zero)
        {
            yaw += delta.x;
            // 마우스를 위로 움직이면 위를 보고, 고개를 끝까지 들어도 뒤집히지 않게 한다.
            pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        Keyboard keyboard = Keyboard.current;
        float vertical = keyboard == null ? 0f : keyboard.spaceKey.ReadValue() - keyboard.shiftKey.ReadValue();

        // 좌우 회전만 적용해서 위나 아래를 보더라도 WASD는 수평으로 이동한다.
        Vector3 input = movementSettings.ReadMoveInput(vertical);
        velocity = movementSettings.UpdateVelocity(velocity, input, yaw, Time.deltaTime);

        ApplyMovementLimits(transform.position + velocity * Time.deltaTime);
    }

    private void ApplyMovementLimits(Vector3 nextPosition)
    {
        Transform rig = cameraBody.transform;
        Vector3 position = rig.position;
        Vector3 remaining = nextPosition - position;
        for (int i = 0; i < 3 && remaining.sqrMagnitude > 0.00000001f; i++)
        {
            float distance = remaining.magnitude;
            Vector3 direction = remaining / distance;
            if (!Physics.SphereCast(position, cameraBody.radius, direction, out RaycastHit hit,
                distance + CollisionSkin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                position += remaining;
                break;
            }
            float travel = Mathf.Clamp(hit.distance - CollisionSkin, 0f, distance);
            position += direction * travel;
            remaining = Vector3.ProjectOnPlane(remaining - direction * travel, hit.normal);
        }
        rig.position = position;
    }
}
