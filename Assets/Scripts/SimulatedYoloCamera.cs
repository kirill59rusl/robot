using UnityEngine;

/// <summary>
/// Симулирует камеру + YOLO-детектор + трекер (BoT-SORT) для ДВУХ классов объектов:
/// мяча (TargetBall) и кубика-цели (Goal).
///
/// Публичный интерфейс для мяча (ballVisible, horizontalOffset, normalizedDistance,
/// viewportPosition) не менялся - RobotBrain использует его как раньше.
///
/// Добавлен аналогичный интерфейс для кубика:
///   cubeVisible, cubeHorizontalOffset, cubeNormalizedDistance, cubeViewportPosition
///
/// Вся логика шума/дропаута/смаза/трекер-персистентности теперь общая
/// для обеих целей (вынесена в TrackState + DetectTarget), но состояние
/// (память кадров, сглаженное значение и т.д.) у каждой цели своё.
///
/// В этой версии:
///   - Motion blur / смаз: при резком манёвре робота реальное значение
///     offset не мгновенно скачет к истинному, а "тащится" за ним
///     с задержкой (имитация того, что YOLO не может точно
///     локализовать размытый объект на смазанном кадре).
///   - On-screen debug overlay: текстом на экране показывает, какой
///     именно эффект сработал ПРЯМО СЕЙЧАС отдельно для мяча и кубика.
/// </summary>
public class SimulatedYoloCamera : MonoBehaviour
{
    [Header("Camera")]
    public Camera robotCamera;

    [Header("Target: Ball")]
    public Transform targetBall;
    [Tooltip("Тег коллайдера мяча для проверки видимости через raycast / автопоиска на сцене")]
    public string ballTag = "TargetBall";

    [Header("Target: Cube (Goal)")]
    public Transform targetCube;
    [Tooltip("Тег коллайдера кубика-цели для проверки видимости через raycast / автопоиска на сцене")]
    public string cubeTag = "Goal";

    [Header("Visibility")]
    public float maxDistance = 2.0f;
    public float horizontalFOV = 40f;

    [Header("Layers")]
    public LayerMask obstacleMask;

    [Header("Шум YOLO / трекера")]
    public bool noiseEnabled = true;

    [Tooltip("Базовая вероятность пропуска детекции за кадр, даже когда объект реально в кадре")]
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

    [Tooltip("Вероятность ложного срабатывания за кадр, когда объекта реально не видно")]
    [Range(0f, 0.05f)] public float falsePositiveChance = 0.01f;

    [Header("Смаз (Motion Blur)")]
    [Tooltip("Насколько сильно резкий манёвр 'размазывает' offset (0 = нет смаза)")]
    [Range(0f, 1f)] public float blurStrength = 0.6f;

    [Tooltip("Скорость, с которой размазанное значение 'догоняет' истинное, когда манёвр прекращается")]
    public float blurRecoverySpeed = 6f;

    [Header("Debug")]
    [Tooltip("Показывать текстовый оверлей с активными эффектами на экране")]
    public bool showDebugOverlay = true;

    // ---------------------------------------------------------------
    // Публичный вывод: МЯЧ (имена сохранены для обратной совместимости)
    // ---------------------------------------------------------------
    [HideInInspector] public bool ballVisible;
    [HideInInspector] public float horizontalOffset;
    [HideInInspector] public float normalizedDistance;
    [HideInInspector] public Vector3 viewportPosition;

    // ---------------------------------------------------------------
    // Публичный вывод: КУБИК
    // ---------------------------------------------------------------
    [HideInInspector] public bool cubeVisible;
    [HideInInspector] public float cubeHorizontalOffset;
    [HideInInspector] public float cubeNormalizedDistance;
    [HideInInspector] public Vector3 cubeViewportPosition;

    /// <summary>
    /// Внутреннее состояние трекера для одной цели (мяча ИЛИ кубика).
    /// У каждой цели - свой независимый экземпляр.
    /// </summary>
    private class TrackState
    {
        public int framesSinceLastSeen;
        public float lastKnownOffset;
        public float lastKnownDistance;
        public float smearedOffset;
        public float noiseSeedOffset;
        public float noiseSeedDistance;

        // debug overlay - что произошло в последнем кадре у ЭТОЙ цели
        public string eventLabel = "OK";
        public Color eventColor = Color.green;
        public float eventTimer;
    }

    private struct DetectionResult
    {
        public bool visible;
        public float offset;
        public float distance;
        public bool viewportUpdated;
        public Vector3 viewport;
    }

    private readonly TrackState ballState = new TrackState();
    private readonly TrackState cubeState = new TrackState();

    void Start()
    {
        if (robotCamera == null)
            robotCamera = GetComponent<Camera>();

        ResolveTarget(ref targetBall, ballTag);
        ResolveTarget(ref targetCube, cubeTag);

        RandomizeSeeds(ballState);
        RandomizeSeeds(cubeState);
    }

    private void RandomizeSeeds(TrackState state)
    {
        state.noiseSeedOffset = Random.Range(0f, 1000f);
        state.noiseSeedDistance = Random.Range(0f, 1000f);
    }

    public void RandomizeNoiseProfile()
    {
        baseDropoutChance = Random.Range(0f, 0.03f);
        jitterAmplitude = Random.Range(0.01f, 0.06f);
        falsePositiveChance = Random.Range(0f, 0.008f);

        RandomizeSeeds(ballState);
        RandomizeSeeds(cubeState);

        ballState.framesSinceLastSeen = 0;
        cubeState.framesSinceLastSeen = 0;
    }

    private void ResolveTarget(ref Transform target, string tag)
    {
        if (target != null || string.IsNullOrEmpty(tag))
            return;

        GameObject found = GameObject.FindGameObjectWithTag(tag);
        if (found != null)
            target = found.transform;
    }

    void Update()
    {
        // Пытаемся заново найти цели, если ссылки потерялись (объект заспавнился позже и т.п.)
        ResolveTarget(ref targetBall, ballTag);
        ResolveTarget(ref targetCube, cubeTag);

        if (robotCamera == null)
        {
            ApplyNotVisible(ballState, hardReset: true);
            ApplyNotVisible(cubeState, hardReset: true);
            ballVisible = false;
            cubeVisible = false;
            return;
        }

        float maneuverIntensity = GetManeuverIntensity();

        // -------- Мяч --------
        if (targetBall == null)
        {
            var r = ApplyNotVisible(ballState, hardReset: true);
            ballVisible = r.visible;
        }
        else
        {
            var r = DetectTarget(targetBall, ballTag, ballState, maneuverIntensity);
            ballVisible = r.visible;
            horizontalOffset = r.offset;
            normalizedDistance = r.distance;
            if (r.viewportUpdated)
                viewportPosition = r.viewport;
        }

        // -------- Кубик --------
        if (targetCube == null)
        {
            var r = ApplyNotVisible(cubeState, hardReset: true);
            cubeVisible = r.visible;
        }
        else
        {
            var r = DetectTarget(targetCube, cubeTag, cubeState, maneuverIntensity);
            cubeVisible = r.visible;
            cubeHorizontalOffset = r.offset;
            cubeNormalizedDistance = r.distance;
            if (r.viewportUpdated)
                cubeViewportPosition = r.viewport;
        }
    }

    /// <summary>
    /// Полный пайплайн детекции одной цели: raycast-видимость -> дропаут/false-positive
    /// -> джиттер -> смаз -> tracker persistence.
    /// </summary>
    private DetectionResult DetectTarget(Transform target, string tag, TrackState state, float maneuverIntensity)
    {
        Vector3 dir = target.position - robotCamera.transform.position;
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
            vp = robotCamera.WorldToViewportPoint(target.position);

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
                if (!hit.collider.CompareTag(tag))
                    rawVisible = false;
            }
        }

        float rawOffset = rawVisible ? (vp.x - 0.5f) * 2f : 0f;

        if (noiseEnabled)
            rawVisible = ApplyDropoutAndFalsePositives(rawVisible, maneuverIntensity, state);

        if (rawVisible)
            return ApplyVisible(rawOffset, rawNormalizedDistance, maneuverIntensity, state, vp);

        return ApplyNotVisible(state, hardReset: false);
    }

    private float GetManeuverIntensity()
    {
        if (drivetrainReference == null)
            return 0f;

        return Mathf.Clamp01(Mathf.Abs(drivetrainReference.steer)) *
               Mathf.Clamp01(Mathf.Abs(drivetrainReference.gas));
    }

    private bool ApplyDropoutAndFalsePositives(bool rawVisible, float maneuverIntensity, TrackState state)
    {
        if (rawVisible)
        {
            float dropoutChance = baseDropoutChance + maneuverIntensity * maneuverDropoutBoost;

            if (Random.value < dropoutChance)
            {
                SetDebugEvent(state, "DROPOUT (моргнула)", new Color(1f, 0.5f, 0f));
                return false;
            }

            return true;
        }
        else
        {
            if (Random.value < falsePositiveChance)
            {
                SetDebugEvent(state, "FALSE POSITIVE", Color.magenta);
                return true;
            }

            return false;
        }
    }

    private DetectionResult ApplyVisible(float rawOffset, float rawDistance, float maneuverIntensity, TrackState state, Vector3 vp)
    {
        state.framesSinceLastSeen = 0;

        float jitterOffset = 0f;
        float jitterDistance = 0f;

        if (noiseEnabled)
        {
            jitterOffset =
                (Mathf.PerlinNoise(state.noiseSeedOffset, Time.time * jitterFrequency) - 0.5f)
                * 2f * jitterAmplitude;

            jitterDistance =
                (Mathf.PerlinNoise(state.noiseSeedDistance, Time.time * jitterFrequency) - 0.5f)
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
            state.smearedOffset = Mathf.Lerp(state.smearedOffset, targetOffset, 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime));

            if (maneuverIntensity * blurStrength > 0.15f &&
                Mathf.Abs(state.smearedOffset - targetOffset) > 0.05f)
            {
                SetDebugEvent(state, "MOTION BLUR (смаз)", Color.cyan);
            }
        }
        else
        {
            state.smearedOffset = targetOffset;
        }

        float finalDistance = Mathf.Clamp01(rawDistance + jitterDistance);

        state.lastKnownOffset = state.smearedOffset;
        state.lastKnownDistance = finalDistance;

        return new DetectionResult
        {
            visible = true,
            offset = state.smearedOffset,
            distance = finalDistance,
            viewportUpdated = true,
            viewport = vp
        };
    }

    private DetectionResult ApplyNotVisible(TrackState state, bool hardReset)
    {
        if (hardReset)
        {
            state.framesSinceLastSeen = 0;
            return new DetectionResult { visible = false, offset = 0f, distance = 0f, viewportUpdated = false };
        }

        state.framesSinceLastSeen++;

        if (noiseEnabled && state.framesSinceLastSeen <= trackerPersistenceFrames)
        {
            SetDebugEvent(state, $"TRACKER HOLD ({state.framesSinceLastSeen}/{trackerPersistenceFrames})", Color.yellow);

            return new DetectionResult
            {
                visible = true,
                offset = state.lastKnownOffset,
                distance = state.lastKnownDistance,
                viewportUpdated = false
            };
        }

        SetDebugEvent(state, "LOST", Color.red);

        return new DetectionResult { visible = false, offset = 0f, distance = 0f, viewportUpdated = false };
    }

    private void SetDebugEvent(TrackState state, string label, Color color)
    {
        state.eventLabel = label;
        state.eventColor = color;
        state.eventTimer = 1.5f; // держим надпись на экране 1.5 сек, чтобы точно успеть увидеть
    }

    void OnGUI()
    {
        if (!showDebugOverlay)
            return;

        var boxStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 10, 6, 6)
        };

        float maneuverIntensity = GetManeuverIntensity();

        GUI.color = Color.white;
        GUI.Box(new Rect(10, 10, 360, 150), "", boxStyle);

        // ---- Мяч ----
        GUI.color = ballVisible ? Color.green : Color.red;
        GUI.Label(new Rect(20, 14, 340, 22),
            $"BALL  visible: {ballVisible}   offset: {horizontalOffset:F2}   dist: {normalizedDistance:F2}", boxStyle);

        DrawEventLabel(ballState, new Rect(20, 38, 340, 22));

        // ---- Кубик ----
        GUI.color = cubeVisible ? Color.green : Color.red;
        GUI.Label(new Rect(20, 66, 340, 22),
            $"CUBE  visible: {cubeVisible}   offset: {cubeHorizontalOffset:F2}   dist: {cubeNormalizedDistance:F2}", boxStyle);

        DrawEventLabel(cubeState, new Rect(20, 90, 340, 22));

        // ---- Общее ----
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 122, 340, 22), $"maneuver intensity: {maneuverIntensity:F2}", boxStyle);

        GUI.color = Color.white;
    }

    private void DrawEventLabel(TrackState state, Rect rect)
    {
        if (state.eventTimer > 0f)
            state.eventTimer -= Time.deltaTime;

        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 10, 6, 6)
        };

        // событие подсвечивается ярко только 1.5 сек после срабатывания,
        // потом гаснет до серого - чтобы было видно именно МОМЕНТ срабатывания
        GUI.color = state.eventTimer > 0f ? state.eventColor : Color.gray;
        GUI.Label(rect, $"  last event: {state.eventLabel}", style);
    }
}