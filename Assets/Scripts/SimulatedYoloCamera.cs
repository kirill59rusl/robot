using UnityEngine;

/// <summary>
/// Симулирует камеру + YOLO-детектор + трекер (BoT-SORT) мяча.
/// Публичный интерфейс (ballVisible, horizontalOffset, normalizedDistance)
/// не менялся - RobotBrain использует его как раньше.
///
/// В этой версии добавлено:
///   - Motion blur / смаз: при резком манёвре робота реальное значение
///     offset не мгновенно скачет к истинному, а "тащится" за ним
///     с задержкой (имитация того, что YOLO не может точно
///     локализовать размытый объект на смазанном кадре).
///   - On-screen debug overlay: текстом на экране показывает, какой
///     именно эффект сработал ПРЯМО СЕЙЧАС - чтобы это было видно
///     физически в Game View, а не искать в инспекторе.
/// </summary>
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

    [Header("Шум YOLO / трекера")]
    public bool noiseEnabled = true;

    [Tooltip("Базовая вероятность пропуска детекции за кадр, даже когда мяч реально в кадре")]
    [Range(0f, 0.2f)] public float baseDropoutChance = 0.05f;

    [Tooltip("Ссылка на TrackController - при резком манёвре растёт вероятность пропуска и смаз")]
    public TrackController drivetrainReference;

    [Tooltip("Насколько сильно резкий манёвр увеличивает вероятность пропуска")]
    public float maneuverDropoutBoost = 0.3f;

    [Tooltip("Сколько кадров подряд 'трекер' держит последнее известное положение")]
    public int trackerPersistenceFrames = 3;

    [Tooltip("Амплитуда дрожания offset/distance")]
    public float jitterAmplitude = 0.03f;

    public float jitterFrequency = 8f;

    [Tooltip("Вероятность ложного срабатывания за кадр, когда мяча реально не видно")]
    [Range(0f, 0.05f)] public float falsePositiveChance = 0.01f;

    [Header("Смаз (Motion Blur)")]
    [Tooltip("Насколько сильно резкий манёвр 'размазывает' offset (0 = нет смаза)")]
    [Range(0f, 1f)] public float blurStrength = 0.6f;

    [Tooltip("Скорость, с которой размазанное значение 'догоняет' истинное, когда манёвр прекращается")]
    public float blurRecoverySpeed = 6f;

    [Header("Debug")]
    [Tooltip("Показывать текстовый оверлей с активными эффектами на экране")]
    public bool showDebugOverlay = true;

    [HideInInspector] public bool ballVisible;
    [HideInInspector] public float horizontalOffset;
    [HideInInspector] public float normalizedDistance;
    [HideInInspector] public Vector3 viewportPosition;

    // внутреннее состояние
    private int framesSinceLastSeen;
    private float lastKnownOffset;
    private float lastKnownDistance;
    private float noiseSeedOffset;
    private float noiseSeedDistance;
    private float smearedOffset; // сглаженное "смазанное" значение

    // для debug overlay - что произошло в последнем кадре
    private string lastEventLabel = "OK";
    private Color lastEventColor = Color.green;
    private float lastEventTimer;

    void Start()
    {
        if (robotCamera == null)
            robotCamera = GetComponent<Camera>();

        if (targetBall == null)
        {
            GameObject ball = GameObject.FindGameObjectWithTag("TargetBall");
            if (ball != null)
                targetBall = ball.transform;
        }

        noiseSeedOffset = Random.Range(0f, 1000f);
        noiseSeedDistance = Random.Range(0f, 1000f);
    }

    public void RandomizeNoiseProfile()
    {
        baseDropoutChance = Random.Range(0f, 0.03f);
        jitterAmplitude = Random.Range(0.01f, 0.06f);
        falsePositiveChance = Random.Range(0f, 0.008f);

        noiseSeedOffset = Random.Range(0f, 1000f);
        noiseSeedDistance = Random.Range(0f, 1000f);

        framesSinceLastSeen = 0;
    }

    void Update()
    {
        if (targetBall == null)
        {
            GameObject ball = GameObject.FindGameObjectWithTag("TargetBall");

            if (ball != null)
            {
                targetBall = ball.transform;
            }
            else
            {
                ApplyNotVisible(hardReset: true);
                return;
            }
        }

        CheckVisibility();
    }

    void CheckVisibility()
    {
        Vector3 dir = targetBall.position - robotCamera.transform.position;
        float distance = dir.magnitude;
        float rawNormalizedDistance = Mathf.Clamp01(distance / maxDistance);

        bool rawVisible = true;

        if (distance > maxDistance)
            rawVisible = false;

        float angle = Vector3.Angle(robotCamera.transform.forward, dir);

        if (rawVisible && angle > horizontalFOV * 0.5f)
            rawVisible = false;

        Vector3 vp = Vector3.zero;

        if (rawVisible)
        {
            vp = robotCamera.WorldToViewportPoint(targetBall.position);

            if (vp.z < 0)
                rawVisible = false;
            else if (vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1)
                rawVisible = false;
        }

        if (rawVisible)
        {
            RaycastHit hit;
            if (Physics.Raycast(robotCamera.transform.position, dir.normalized, out hit, maxDistance))
            {
                if (!hit.collider.CompareTag("TargetBall"))
                    rawVisible = false;
            }
        }

        float rawOffset = rawVisible ? (vp.x - 0.5f) * 2f : 0f;

        // текущая "резкость манёвра" - используется и для смаза, и для роста dropout
        float maneuverIntensity = GetManeuverIntensity();

        if (noiseEnabled)
            rawVisible = ApplyDropoutAndFalsePositives(rawVisible, maneuverIntensity);

        if (rawVisible)
        {
            viewportPosition = vp;
            ApplyVisible(rawOffset, rawNormalizedDistance, maneuverIntensity);
        }
        else
        {
            ApplyNotVisible(hardReset: false);
        }
    }

    private float GetManeuverIntensity()
    {
        if (drivetrainReference == null)
            return 0f;

        return Mathf.Clamp01(Mathf.Abs(drivetrainReference.steer)) *
               Mathf.Clamp01(Mathf.Abs(drivetrainReference.gas));
    }

    private bool ApplyDropoutAndFalsePositives(bool rawVisible, float maneuverIntensity)
    {
        if (rawVisible)
        {
            float dropoutChance = baseDropoutChance + maneuverIntensity * maneuverDropoutBoost;

            if (Random.value < dropoutChance)
            {
                SetDebugEvent("DROPOUT (моргнула)", new Color(1f, 0.5f, 0f));
                return false;
            }

            return true;
        }
        else
        {
            if (Random.value < falsePositiveChance)
            {
                SetDebugEvent("FALSE POSITIVE", Color.magenta);
                return true;
            }

            return false;
        }
    }

    private void ApplyVisible(float rawOffset, float rawDistance, float maneuverIntensity)
    {
        framesSinceLastSeen = 0;

        float jitterOffset = 0f;
        float jitterDistance = 0f;

        if (noiseEnabled)
        {
            jitterOffset =
                (Mathf.PerlinNoise(noiseSeedOffset, Time.time * jitterFrequency) - 0.5f)
                * 2f * jitterAmplitude;

            jitterDistance =
                (Mathf.PerlinNoise(noiseSeedDistance, Time.time * jitterFrequency) - 0.5f)
                * 2f * jitterAmplitude;
        }

        float targetOffset = Mathf.Clamp(rawOffset + jitterOffset, -1f, 1f);

        // --- Смаз (motion blur) ---
        // Чем резче манёвр, тем медленнее "смазанное" значение
        // догоняет истинное - имитация того, что YOLO не успевает
        // точно локализовать объект на размазанном кадре.
        if (noiseEnabled && blurStrength > 0f)
        {
            float lerpSpeed = Mathf.Lerp(blurRecoverySpeed, blurRecoverySpeed * 0.1f, maneuverIntensity * blurStrength);
            smearedOffset = Mathf.Lerp(smearedOffset, targetOffset, 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime));

            if (maneuverIntensity * blurStrength > 0.15f &&
                Mathf.Abs(smearedOffset - targetOffset) > 0.05f)
            {
                SetDebugEvent("MOTION BLUR (смаз)", Color.cyan);
            }
        }
        else
        {
            smearedOffset = targetOffset;
        }

        horizontalOffset = smearedOffset;
        normalizedDistance = Mathf.Clamp01(rawDistance + jitterDistance);
        ballVisible = true;

        lastKnownOffset = horizontalOffset;
        lastKnownDistance = normalizedDistance;
    }

    private void ApplyNotVisible(bool hardReset)
    {
        if (hardReset)
        {
            ballVisible = false;
            framesSinceLastSeen = 0;
            return;
        }

        framesSinceLastSeen++;

        if (noiseEnabled && framesSinceLastSeen <= trackerPersistenceFrames)
        {
            ballVisible = true;
            horizontalOffset = lastKnownOffset;
            normalizedDistance = lastKnownDistance;
            SetDebugEvent($"TRACKER HOLD ({framesSinceLastSeen}/{trackerPersistenceFrames})", Color.yellow);
        }
        else
        {
            ballVisible = false;
            SetDebugEvent("LOST", Color.red);
        }
    }

    private void SetDebugEvent(string label, Color color)
    {
        lastEventLabel = label;
        lastEventColor = color;
        lastEventTimer = 1.5f; // держим надпись на экране 1.5 сек, чтобы точно успеть увидеть
    }

    void OnGUI()
    {
        if (!showDebugOverlay)
            return;

        if (lastEventTimer > 0f)
            lastEventTimer -= Time.deltaTime;

        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 10, 6, 6)
        };

        GUI.color = Color.white;
        GUI.Box(new Rect(10, 10, 340, 90), "", style);

        GUI.color = ballVisible ? Color.green : Color.red;
        GUI.Label(new Rect(20, 15, 320, 24),
            $"ballVisible: {ballVisible}   offset: {horizontalOffset:F2}   dist: {normalizedDistance:F2}", style);

        // событие подсвечивается ярко только 1.5 сек после срабатывания,
        // потом гаснет до серого - чтобы было видно именно МОМЕНТ срабатывания
        Color fade = lastEventTimer > 0f ? lastEventColor : Color.gray;
        GUI.color = fade;
        GUI.Label(new Rect(20, 45, 320, 24), $"last event: {lastEventLabel}", style);

        float maneuverIntensity = GetManeuverIntensity();
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 70, 320, 24), $"maneuver intensity: {maneuverIntensity:F2}", style);

        GUI.color = Color.white;
    }
}