using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Имитирует телеметрию RealVision.cs (UDP от Python YOLO-ноды),
/// но во время обучения в Unity ML-Agents. Интерфейс намеренно
/// идентичен RealVision, чтобы RobotBrain можно было переключать
/// между "SimulatedVision" (тренировка) и "RealVision" (реальный робот)
/// без изменения остального кода.
///
/// Задача: агент, обученный ТОЛЬКО на идеальных данных (истинная
/// позиция мяча без шума), в реальности ломается о проблемы,
/// описанные в yolo_vision_node.py:
///   - потеря объекта на 2-3 кадра при повороте/засветке
///   - дрожание bounding box (даже когда мяч неподвижен)
///   - скачки confidence
///   - сетевые задержки/потери UDP-пакетов
///   - ложные срабатывания на постороннем оранжевом объекте
///
/// Этот скрипт воспроизводит все эти эффекты поверх "чистой" истины
/// из симуляции, чтобы RL-политика с самого начала обучалась терпеть
/// шум, а не полагаться на идеальный сигнал.
/// </summary>
public class SimulatedVision : MonoBehaviour
{
    [Header("Ground truth (симуляция)")]
    [Tooltip("Трансформ мяча в сцене")]
    public Transform ball;

    [Tooltip("Точка/трансформ камеры робота (для угла и дальности). " +
             "Если не задано - используется сам этот transform.")]
    public Transform cameraTransform;

    [Tooltip("Горизонтальный угол обзора камеры в градусах (для нормализации angle, как FOV реальной камеры)")]
    public float cameraFovDegrees = 70f;

    [Tooltip("Дальность, на которой bbox считается 'на весь кадр' (distance -> 1.0)")]
    public float maxCloseDistance = 0.3f;

    [Tooltip("Дальность, дальше которой мяч считается вне досягаемости детекции (distance -> ~0)")]
    public float maxVisibleDistance = 4f;

    [Tooltip("Слои, которые блокируют линию видимости (стены/препятствия/собственная клешня робота)")]
    public LayerMask occlusionMask;

    [Header("Телеметрия (совпадает по смыслу с RealVision)")]
    public bool useYOLO = true;
    public float normalizedAngle;
    public float normalizedDistance;
    public bool seesBall;
    public float conf;

    [Header("Domain Randomization: шум зрения")]
    [Tooltip("Включить симуляцию шума. Выключайте для отладки/чистого прогона.")]
    public bool noiseEnabled = true;

    [Tooltip("Базовая вероятность 'ослепнуть' на кадр, даже если мяч в кадре (0..1 за FixedUpdate)")]
    [Range(0f, 0.2f)] public float baseDropoutChance = 0.01f;

    [Tooltip("Насколько резкий поворот/газ робота увеличивает вероятность пропуска детекции " +
             "(блик/смаз при быстром развороте)")]
    public float maneuverDropoutBoost = 0.15f;

    [Tooltip("Ссылка на контроллер гусениц - чтобы шум рос при резких манёврах")]
    public TrackController drivetrainReference;

    [Tooltip("Сколько кадров подряд трекер 'достраивает' положение по инерции (BoT-SORT/Калман), " +
             "прежде чем честно сказать seesBall=false")]
    public int trackerPersistenceFrames = 3;

    [Tooltip("Амплитуда дрожания angle/distance (имитация нестабильности bbox по пикселям)")]
    public float jitterAmplitude = 0.03f;

    [Tooltip("Частота дрожания (выше = более резкие мелкие скачки)")]
    public float jitterFrequency = 8f;

    [Tooltip("Амплитуда шума confidence")]
    public float confNoiseAmplitude = 0.15f;

    [Tooltip("Вероятность ложного срабатывания за кадр, когда мяча реально не видно " +
             "(похоже на посторонний оранжевый объект)")]
    [Range(0f, 0.05f)] public float falsePositiveChance = 0.003f;

    [Header("Domain Randomization: сеть (UDP-подобная задержка)")]
    [Tooltip("Имитация задержки пакета в кадрах FixedUpdate (0 = без задержки)")]
    public int minLatencyFrames = 0;
    public int maxLatencyFrames = 3;

    [Tooltip("Вероятность 'потери пакета' за кадр (UDP не гарантирует доставку - " +
             "тогда просто не обновляем телеметрию в этом кадре, как в реальном коде)")]
    [Range(0f, 0.1f)] public float packetLossChance = 0.02f;

    // --- внутреннее состояние ---
    private struct RawSample
    {
        public bool visible;
        public float angle;
        public float distance;
        public float confidence;
    }

    private Queue<RawSample> latencyQueue = new Queue<RawSample>();
    private int framesSinceLastSeen;
    private float lastKnownAngle;
    private float lastKnownDistance;
    private float noiseSeedAngle;
    private float noiseSeedDist;

    private void Awake()
    {
        if (cameraTransform == null)
            cameraTransform = transform;

        // разные семена шума на каждый экземпляр/эпизод
        noiseSeedAngle = Random.Range(0f, 1000f);
        noiseSeedDist = Random.Range(0f, 1000f);
    }

    /// <summary>
    /// Вызывайте это в Agent.OnEpisodeBegin(), чтобы каждый эпизод обучения
    /// агент видел ЧУТЬ РАЗНЫЙ уровень шума камеры (domain randomization) -
    /// иначе он выучит particular шум, а не устойчивость к шуму вообще.
    /// </summary>
    public void RandomizeNoiseProfile()
    {
        baseDropoutChance = Random.Range(0.0f, 0.03f);
        jitterAmplitude = Random.Range(0.01f, 0.06f);
        confNoiseAmplitude = Random.Range(0.05f, 0.25f);
        falsePositiveChance = Random.Range(0f, 0.008f);
        packetLossChance = Random.Range(0f, 0.05f);
        maxLatencyFrames = Random.Range(0, 5);

        noiseSeedAngle = Random.Range(0f, 1000f);
        noiseSeedDist = Random.Range(0f, 1000f);

        framesSinceLastSeen = 0;
        latencyQueue.Clear();
    }

    private void FixedUpdate()
    {
        RawSample sample = ComputeGroundTruth();

        if (noiseEnabled)
        {
            sample = ApplyDropoutAndFalsePositives(sample);
        }

        // сетевая задержка / потеря пакета - кладём в очередь и читаем с опозданием,
        // как реальный UDP-джиттер
        if (noiseEnabled && (packetLossChance > 0f || maxLatencyFrames > 0))
        {
            if (Random.value < packetLossChance)
            {
                // "пакет потерян" - просто не кладём новый сэмпл в очередь в этом кадре,
                // телеметрия останется прежней (как реальный RealVision.Update без нового пакета)
            }
            else
            {
                latencyQueue.Enqueue(sample);
            }

            int targetDelay = Random.Range(minLatencyFrames, maxLatencyFrames + 1);

            while (latencyQueue.Count > Mathf.Max(1, targetDelay))
            {
                sample = latencyQueue.Dequeue();
                ApplySampleToTelemetry(sample);
            }

            // если очередь ещё не "дозрела" - телеметрия не обновляется в этом кадре
            return;
        }

        ApplySampleToTelemetry(sample);
    }

    private RawSample ComputeGroundTruth()
    {
        if (ball == null)
        {
            return new RawSample { visible = false, angle = 0f, distance = 1f, confidence = 0f };
        }

        Vector3 toBall = ball.position - cameraTransform.position;
        toBall.y = 0f;

        float distance = toBall.magnitude;

        // угол относительно "вперёд" камеры, нормализованный на FOV (как x_norm в Python-ноде)
        float signedAngleDeg = Vector3.SignedAngle(cameraTransform.forward, toBall.normalized, Vector3.up);
        float angleNorm = Mathf.Clamp(signedAngleDeg / (cameraFovDegrees * 0.5f), -1f, 1f);

        bool withinFov = Mathf.Abs(signedAngleDeg) <= cameraFovDegrees * 0.5f;
        bool withinRange = distance <= maxVisibleDistance;

        bool losClear = true;
        if (withinFov && withinRange)
        {
            if (Physics.Raycast(cameraTransform.position, toBall.normalized, out RaycastHit hit, distance, occlusionMask))
            {
                // что-то загораживает мяч раньше, чем сам мяч
                losClear = false;
            }
        }

        bool visible = withinFov && withinRange && losClear;

        // distance_norm: чем ближе, тем ближе к 1.0 (как высота bbox / высота кадра)
        float distNorm = Mathf.InverseLerp(maxVisibleDistance, maxCloseDistance, distance);
        distNorm = Mathf.Clamp01(distNorm);

        float confidence = visible
            ? Mathf.Clamp01(1f - (distance / maxVisibleDistance) * 0.5f)
            : 0f;

        return new RawSample
        {
            visible = visible,
            angle = angleNorm,
            distance = distNorm,
            confidence = confidence
        };
    }

    private RawSample ApplyDropoutAndFalsePositives(RawSample sample)
    {
        if (sample.visible)
        {
            float dropoutChance = baseDropoutChance;

            if (drivetrainReference != null)
            {
                float maneuverIntensity =
                    Mathf.Clamp01(Mathf.Abs(drivetrainReference.steer)) *
                    Mathf.Clamp01(Mathf.Abs(drivetrainReference.gas));

                dropoutChance += maneuverIntensity * maneuverDropoutBoost;
            }

            if (Random.value < dropoutChance)
            {
                // YOLO "моргнула" - объект технически на месте, но детектор его не поймал
                sample.visible = false;
            }
        }
        else
        {
            // ложное срабатывание - редкая имитация постороннего похожего объекта
            if (Random.value < falsePositiveChance)
            {
                sample.visible = true;
                sample.angle = Random.Range(-1f, 1f);
                sample.distance = Random.Range(0.1f, 0.5f);
                sample.confidence = Random.Range(0.2f, 0.45f); // обычно ложные срабатывания менее уверенные
            }
        }

        return sample;
    }

    private void ApplySampleToTelemetry(RawSample sample)
    {
        useYOLO = true;

        if (sample.visible)
        {
            framesSinceLastSeen = 0;

            float jitterA = 0f;
            float jitterD = 0f;
            float confNoise = 0f;

            if (noiseEnabled)
            {
                jitterA = (Mathf.PerlinNoise(noiseSeedAngle, Time.time * jitterFrequency) - 0.5f) * 2f * jitterAmplitude;
                jitterD = (Mathf.PerlinNoise(noiseSeedDist, Time.time * jitterFrequency) - 0.5f) * 2f * jitterAmplitude;
                confNoise = Random.Range(-confNoiseAmplitude, confNoiseAmplitude);
            }

            normalizedAngle = Mathf.Clamp(sample.angle + jitterA, -1f, 1f);
            normalizedDistance = Mathf.Clamp01(sample.distance + jitterD);
            conf = Mathf.Clamp01(sample.confidence + confNoise);

            seesBall = true;

            lastKnownAngle = normalizedAngle;
            lastKnownDistance = normalizedDistance;
        }
        else
        {
            framesSinceLastSeen++;

            if (noiseEnabled && framesSinceLastSeen <= trackerPersistenceFrames)
            {
                // имитация трекера (BoT-SORT/Калман): пару кадров "достраиваем"
                // последнее известное положение вместо мгновенной потери цели
                seesBall = true;
                normalizedAngle = lastKnownAngle;
                normalizedDistance = lastKnownDistance;
                conf = Mathf.Max(0.05f, conf - 0.1f); // уверенность падает, пока держим по инерции
            }
            else
            {
                seesBall = false;
                normalizedAngle = 0f;
                normalizedDistance = 1f;
                conf = 0f;
            }
        }
    }
}