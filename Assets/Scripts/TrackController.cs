using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Управление гусеницами GFS-X:
/// дифференциал → PWM → MovePosition/MoveRotation.
/// WASD управление:
/// W/S - газ
/// A/D - поворот
///
/// Добавлено:
/// - Drift: часть скорости "проскальзывает" вбок вместо
///   мгновенного изменения направления (инерция гусениц).
/// - Random deviation: лёгкий шум курса, имитирующий
///   неровности грунта / рассинхрон гусениц.
/// - Friction: затухание скорости и заноса, когда газ
///   отпущен или ниже порога сцепления.
/// </summary>

[RequireComponent(typeof(Rigidbody))]
public class TrackController : MonoBehaviour
{
    [Header("Калибровка")]
    public float moveSpeed = 1f;
    public float turnSpeed = 120f;
    [Range(0f, 1f)]
    public float turnK = 0.30f;

    public float maxLinearCmd = 0.25f;

    [Header("PWM")]
    public float speedToPwm = 200f;
    public float motorDeadzone = 30f;
    public float minMotorPwm = 50f;
    public float maxPwmStep = 15f;

    [Header("Drift / Занос")]
    [Tooltip("0 = нет заноса (жёсткое сцепление), 1 = сильный занос")]
    [Range(0f, 1f)]
    public float driftFactor = 0.35f;

    [Tooltip("Насколько быстро боковая (заносная) скорость гасится трением")]
    public float lateralFriction = 4f;

    [Tooltip("При каком угле поворота (град/сек) начинается занос")]
    public float driftYawThreshold = 40f;

    [Header("Случайное отклонение")]
    [Tooltip("Сила случайного шатания курса (град/сек)")]
    public float randomYawNoiseStrength = 3f;

    [Tooltip("Частота изменения шума (выше = более резкие рывки)")]
    public float randomNoiseFrequency = 0.5f;

    [Header("Трение / затухание")]
    [Tooltip("Затухание продольной скорости, когда газ = 0")]
    public float longitudinalFriction = 2.5f;

    [Tooltip("Общий коэффициент трения качения (снижает эффективную скорость всегда)")]
    [Range(0f, 1f)]
    public float rollingFriction = 0.05f;

    [Header("Команды (-1...1)")]
    public float gas;
    public float steer;

    [Header("ROS")]
    public bool useRealRobot = false;
    public RosBridge rosBridge;

    private Rigidbody rb;

    private float prevLeftPwm;
    private float prevRightPwm;

    // текущая продольная и боковая (заносная) скорость - для инерции/дрифта
    private float currentLongitudinalVel;
    private float currentLateralVel;

    // текущая скорость поворота (для сглаживания и заноса)
    private float currentYawRate;

    // сиды шума для каждого экземпляра, чтобы разные машины "гуляли" по-разному
    private float noiseSeed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.interpolation =
            RigidbodyInterpolation.Interpolate;

        rb.collisionDetectionMode =
            CollisionDetectionMode.Continuous;

        rb.linearDamping = 9f;
        rb.angularDamping = 9f;

        rb.constraints |=
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        noiseSeed = Random.Range(0f, 1000f);
    }

    private void Update()
    {
        ReadKeyboard();
    }

    private void ReadKeyboard()
    {
        gas = 0f;
        steer = 0f;

        if (Keyboard.current == null)
            return;

        // газ

        if (Keyboard.current.wKey.isPressed)
            gas += 1f;

        if (Keyboard.current.sKey.isPressed)
            gas -= 1f;

        // поворот

        if (Keyboard.current.aKey.isPressed)
            steer -= 1f;

        if (Keyboard.current.dKey.isPressed)
            steer += 1f;
    }

    private void FixedUpdate()
    {
        float g =
            Mathf.Clamp(
                gas,
                -1f,
                1f
            );

        float s =
            Mathf.Clamp(
                steer,
                -1f,
                1f
            );

        MixTracks(
            g,
            s,
            out float vLeft,
            out float vRight
        );

        if (useRealRobot)
        {
            if (rosBridge != null)
                rosBridge.SendVelocity(g, s);

            return;
        }

        float leftPwm =
            ApplyMotorModel(
                vLeft * speedToPwm,
                ref prevLeftPwm
            );

        float rightPwm =
            ApplyMotorModel(
                vRight * speedToPwm,
                ref prevRightPwm
            );

        float effLeft =
            leftPwm / speedToPwm;

        float effRight =
            rightPwm / speedToPwm;

        float v =
            0.5f *
            (effRight + effLeft);

        float diff =
            effRight - effLeft;

        float targetYawDegPerSec =
            (diff /
            (2f * Mathf.Max(moveSpeed, 1e-4f)))
            * turnSpeed;

        float dt =
            Time.fixedDeltaTime;

        // --- Случайное отклонение (шум курса) ---
        // Шум должен быть только при реальном движении гусениц.
        // Если газ = 0 (машина стоит), шум = 0 -> никакого самопроизвольного
        // шатания на месте.
        float movementIntensity =
            Mathf.Clamp01(
                Mathf.Abs(g) // берём именно ввод газа, а не текущую скорость,
                             // чтобы шум пропадал сразу при отпускании клавиши,
                             // а не постепенно вместе с инерцией
            );

        float noise = 0f;

        if (movementIntensity > 0.01f)
        {
            // Perlin даёт гладкий, не дёрганый шум вместо белого рандома.
            noise =
                (Mathf.PerlinNoise(
                    noiseSeed,
                    Time.time * randomNoiseFrequency
                ) - 0.5f) * 2f;
        }

        float noiseYaw =
            noise * randomYawNoiseStrength * movementIntensity;

        targetYawDegPerSec += noiseYaw;

        // сглаживаем реальную скорость поворота к целевой (инерция поворота)
        currentYawRate =
            Mathf.Lerp(
                currentYawRate,
                targetYawDegPerSec,
                1f - Mathf.Exp(-6f * dt)
            );

        // --- Продольная скорость с трением (инерция разгона/торможения) ---
        float targetLongitudinal = v;

        currentLongitudinalVel =
            Mathf.MoveTowards(
                currentLongitudinalVel,
                targetLongitudinal,
                longitudinalFriction * dt
            );

        // трение качения всегда чуть подъедает скорость
        currentLongitudinalVel *=
            (1f - rollingFriction * dt);

        // --- Занос (drift) ---
        // Если поворот резкий и скорость поворота высокая - часть энергии
        // уходит не в поворот корпуса, а в боковое проскальзывание.
        float yawExcess =
            Mathf.Max(
                0f,
                Mathf.Abs(currentYawRate) - driftYawThreshold
            );

        float driftInput =
            Mathf.Sign(currentYawRate) *
            yawExcess *
            driftFactor *
            0.01f *
            Mathf.Abs(currentLongitudinalVel);

        currentLateralVel =
            Mathf.MoveTowards(
                currentLateralVel,
                driftInput,
                lateralFriction * dt
            );

        // боковая скорость сама по себе гасится трением о грунт
        currentLateralVel *=
            (1f - lateralFriction * dt);

        // --- Итоговое смещение ---
        // transform.right используется как "вперёд" (как и в исходнике)
        Vector3 forwardDir = transform.right;
        Vector3 lateralDir = transform.forward; // перпендикуляр к "вперёд" в горизонтальной плоскости

        Vector3 delta =
            forwardDir * (currentLongitudinalVel * dt) +
            lateralDir * (currentLateralVel * dt);

        delta.y = 0f;

        rb.MovePosition(
            rb.position + delta
        );

        rb.MoveRotation(
            rb.rotation *
            Quaternion.Euler(
                0f,
                currentYawRate * dt,
                0f
            )
        );
    }

    private void MixTracks(
        float g,
        float s,
        out float vLeft,
        out float vRight
    )
    {
        // moveSpeed - скорость симуляции (внутренняя калибровка),
        // maxLinearCmd - ограничение ROS-команды (внешний физический предел).
        // Сначала берём эффективный (реально достижимый) предел скорости,
        // а уже потом линейно умножаем на газ - иначе при moveSpeed > maxLinearCmd
        // отклик на газ перестаёт быть линейным и "залипает" на максимуме
        // задолго до полного хода стика/клавиши.
        float effectiveMaxSpeed =
            Mathf.Min(moveSpeed, maxLinearCmd);

        float linear =
            g * effectiveMaxSpeed;

        float turn =
            s *
            turnK *
            moveSpeed;

        vLeft =
            Mathf.Clamp(
                linear - turn,
                -moveSpeed,
                moveSpeed
            );

        vRight =
            Mathf.Clamp(
                linear + turn,
                -moveSpeed,
                moveSpeed
            );
    }

    private float ApplyMotorModel(
        float rawPwm,
        ref float previous
    )
    {
        float sign =
            Mathf.Sign(rawPwm);

        float mag =
            Mathf.Abs(rawPwm);

        if (mag < motorDeadzone)
        {
            mag = 0f;
        }
        else if (mag < minMotorPwm)
        {
            mag = minMotorPwm;
        }

        mag =
            Mathf.Min(
                mag,
                100f
            );

        float target =
            sign * mag;

        previous +=
            Mathf.Clamp(
                target - previous,
                -maxPwmStep,
                maxPwmStep
            );

        return previous;
    }

    private void CalculatePWM(
        float gas,
        float steer,
        out float leftPwm,
        out float rightPwm)
    {
        MixTracks(
            gas,
            steer,
            out float vLeft,
            out float vRight);

        leftPwm =
            ApplyMotorModel(
                vLeft * speedToPwm,
                ref prevLeftPwm);

        rightPwm =
            ApplyMotorModel(
                vRight * speedToPwm,
                ref prevRightPwm);
    }
}