using UnityEngine;
using UnityEngine.Rendering;

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
        if (rosBridge != null) rosBridge.SendCamera(-0.3f);
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
            float normalized = -0.3f + currentAngle / maxAngle; // -1..1
            normalized=Mathf.Clamp(normalized,-1, 0.4f);
            rosBridge.SendCamera(normalized);
        }
    }
}