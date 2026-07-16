using UnityEngine;

public class SimulatedYoloCamera : MonoBehaviour
{
    [Header("Camera")]
    public Camera robotCamera;

    [Header("Target")]
    public Transform targetBall;

    [Header("Visibility")]
    public float maxDistance = 2.0f;
    public float horizontalFOV = 40f;

    [Header("Layers")]
    public LayerMask obstacleMask;

    [HideInInspector]
    public bool ballVisible;

    [HideInInspector]
    public float horizontalOffset;

    [HideInInspector]
    public float normalizedDistance;

    [HideInInspector]
    public Vector3 viewportPosition;

    void Start()
    {
        if (robotCamera == null)
            robotCamera = GetComponent<Camera>();

        if (targetBall == null)
        {
            GameObject ball =
                GameObject.FindGameObjectWithTag("TargetBall");

            if (ball != null)
                targetBall = ball.transform;
        }
    }

    void Update()
    {
        if (targetBall == null)
        {
            GameObject ball =
                GameObject.FindGameObjectWithTag("TargetBall");

            if (ball != null){
                targetBall = ball.transform;}
            else
        {
            ballVisible = false;
            return;
        }
            ballVisible = false;
            return;
        }

        CheckVisibility();
    }

    void CheckVisibility()
    {
        Vector3 dir =
            targetBall.position - robotCamera.transform.position;

        float distance = dir.magnitude;

        normalizedDistance =
            Mathf.Clamp01(distance / maxDistance);

        if (distance > maxDistance)
        {
            ballVisible = false;
            return;
        }

        float angle =
            Vector3.Angle(
                robotCamera.transform.forward,
                dir
            );

        if (angle > horizontalFOV * 0.5f)
        {
            ballVisible = false;
            return;
        }

        viewportPosition =
            robotCamera.WorldToViewportPoint(
                targetBall.position
            );

        if (viewportPosition.z < 0)
        {
            ballVisible = false;
            return;
        }

        if (viewportPosition.x < 0 ||
            viewportPosition.x > 1 ||
            viewportPosition.y < 0 ||
            viewportPosition.y > 1)
        {
            ballVisible = false;
            return;
        }

        RaycastHit hit;

        if (Physics.Raycast(
            robotCamera.transform.position,
            dir.normalized,
            out hit,
            maxDistance))
        {
            if (!hit.collider.CompareTag("TargetBall"))
            {
                ballVisible = false;
                return;
            }
        }

        horizontalOffset =
            (viewportPosition.x - 0.5f) * 2f;

        ballVisible = true;
    }
}