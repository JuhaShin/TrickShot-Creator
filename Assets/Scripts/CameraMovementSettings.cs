using UnityEngine;

public sealed class CameraMovementSettings : ScriptableObject
{
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Range(0f, 1f)] private float rotationMultiplier = 1f;

    public float DefaultSensitivity => rotationMultiplier;

    public Vector3 ReadMoveInput(float vertical = 0f) =>
        new Vector3(Input.GetAxis("Horizontal"), vertical, Input.GetAxis("Vertical"));

    public Vector2 ReadLookDelta() => Input.mousePresent
        ? new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * (10f * SettingsWindow.Sensitivity(rotationMultiplier))
        : Vector2.zero;

    public Vector3 UpdateVelocity(Vector3 velocity, Vector3 input, float yaw, float deltaTime)
    {
        float speed = moveSpeed * 0.5f;
        if (input != Vector3.zero)
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.ClampMagnitude(input, 1f) * speed;
        return Vector3.MoveTowards(velocity, Vector3.zero, speed / 0.2f * deltaTime);
    }
}
