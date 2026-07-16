using UnityEngine;

public class CameraPanController : MonoBehaviour
{
    public float maxAngle = 90f;

    public float rotationSpeed = 120f;

    float targetAngle;

    public float CurrentAngle =>
        targetAngle;

    public void SetInput(float input)
    {
        targetAngle =
            Mathf.Clamp(
                targetAngle +
                input * rotationSpeed * Time.fixedDeltaTime,
                -maxAngle,
                maxAngle
            );
    }

    void LateUpdate()
    {
        transform.localRotation =
            Quaternion.Euler(
                0,
                targetAngle,
                0
            );
    }
}