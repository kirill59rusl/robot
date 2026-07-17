using UnityEngine;

public class CameraPanController : MonoBehaviour
{
    [Header("Camera")]
    public float maxAngle = 90f;
    public float rotationSpeed = 120f;

    [Header("ROS")]
    public RosBridge rosBridge;

    float targetAngle;
    float currentAngle;

    public float CurrentAngle => currentAngle;

    void Start()
    {
        if (rosBridge == null)
            rosBridge = FindFirstObjectByType<RosBridge>();

        targetAngle = 0f;
        currentAngle = 0f;
    }

    /// <summary>
    /// Вызывается агентом или Heuristic.
    /// input = -1..1
    /// </summary>
    public void SetInput(float input)
    {
        targetAngle += input * rotationSpeed * Time.fixedDeltaTime;
        targetAngle = Mathf.Clamp(targetAngle, -maxAngle, maxAngle);
    }

    void FixedUpdate()
    {
        // Плавное движение камеры
        currentAngle = Mathf.MoveTowards(
            currentAngle,
            targetAngle,
            rotationSpeed * Time.fixedDeltaTime);

        transform.localRotation = Quaternion.Euler(
            0f,
            currentAngle,
            0f);

        // Отправляем угол на реального робота
        if (rosBridge != null)
        {
            float normalized = currentAngle / maxAngle; // -1..1
            rosBridge.SendCamera(normalized);
        }
    }
}