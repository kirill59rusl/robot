using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Симулирует реальные артефакты видеопотока с камеры робота,
/// которые НЕ должны "пугать"/ломать YOLO-детектор, а должны
/// использоваться как augmentation / стресс-тест устойчивости:
///
/// - Frame drop: камера на пару кадров "замирает" (повтор предыдущего кадра)
/// - Frame loss / blackout: короткая полная потеря кадра (чёрный/шумный кадр)
/// - Засвет (overexposure flash): яркая вспышка, например от солнца/фонаря
/// - Motion blur burst: смаз при резком повороте/тряске
/// - Шум сенсора (grain/noise): типичный шум при слабом освещении
/// - Блочные артефакты (compression glitch): "рассыпание" картинки как при
///   плохом Wi-Fi/аналоговом видео канале
///
/// Использование:
/// Повесить на GameObject с RawImage, который показывает то, что "видит"
/// YOLO (либо сам поток с камеры, либо копию RenderTexture). Скрипт кладёт
/// поверх UI-оверлей нужный эффект. Сама YOLO-модель должна получать кадры
/// ПОСЛЕ применения этого оверлея, если вы тестируете её устойчивость к
/// таким сбоям.
/// </summary>
public class YoloFeedGlitchSimulator : MonoBehaviour
{
    [Header("Источник видео (опционально)")]
    [Tooltip("Камера, с которой идёт поток на YOLO. " +
             "Нужна только для эффекта frame drop (кратковременное отключение рендера).")]
    public Camera sourceCamera;

    [Tooltip("UI-оверлей поверх видео (создайте RawImage на весь экран/кадр, " +
             "растяните на весь родительский RectTransform, alpha управляется скриптом).")]
    public RawImage overlay;

    [Header("Частота сбоев (события в секунду, примерно)")]
    [Tooltip("Общая частота случайных глитчей. 0.05 = один глитч примерно раз в 20 секунд.")]
    public float glitchRatePerSecond = 0.05f;

    [Header("Веса типов сбоев (относительные, не обязаны давать в сумме 1)")]
    [Range(0f, 1f)] public float weightFrameDrop = 0.30f;
    [Range(0f, 1f)] public float weightFrameLoss = 0.15f;
    [Range(0f, 1f)] public float weightOverexposure = 0.20f;
    [Range(0f, 1f)] public float weightMotionBlur = 0.20f;
    [Range(0f, 1f)] public float weightNoiseGrain = 0.10f;
    [Range(0f, 1f)] public float weightCompressionGlitch = 0.05f;

    [Header("Длительность эффектов (сек)")]
    public Vector2 frameDropDuration = new Vector2(0.03f, 0.12f);   // 1-4 кадра примерно
    public Vector2 frameLossDuration = new Vector2(0.05f, 0.2f);
    public Vector2 overexposureDuration = new Vector2(0.1f, 0.4f);
    public Vector2 motionBlurDuration = new Vector2(0.1f, 0.3f);
    public Vector2 noiseGrainDuration = new Vector2(0.3f, 1.0f);
    public Vector2 compressionGlitchDuration = new Vector2(0.05f, 0.25f);

    [Header("Привязка к движению (реалистичность)")]
    [Tooltip("Если задано, motion blur / глитчи учащаются при резких манёврах " +
             "(подключите сюда TrackController, чтобы читать его текущий занос/поворот).")]
    public TrackController drivetrainReference;

    [Tooltip("Насколько сильно резкий манёвр увеличивает вероятность сбоя (0 = не влияет)")]
    public float maneuverGlitchBoost = 3f;

    private Texture2D noiseTexture;
    private Coroutine activeGlitchRoutine;
    private bool isGlitchActive;

    private void Awake()
    {
        // Небольшая процедурная текстура шума для эффекта grain/помех,
        // чтобы не тащить внешние ассеты.
        noiseTexture = GenerateNoiseTexture(64, 64);

        if (overlay != null)
        {
            overlay.color = new Color(1f, 1f, 1f, 0f);
            overlay.raycastTarget = false;
        }
    }

    private void Update()
    {
        if (isGlitchActive)
            return;

        float rate = glitchRatePerSecond;

        // если машину заносит/резко крутит - реалистично, что и картинка чаще "дёргается"
        if (drivetrainReference != null)
        {
            float maneuverIntensity =
                Mathf.Clamp01(Mathf.Abs(drivetrainReference.steer)) *
                Mathf.Clamp01(Mathf.Abs(drivetrainReference.gas));

            rate += maneuverIntensity * maneuverGlitchBoost * glitchRatePerSecond;
        }

        // вероятность события в этом кадре, исходя из "событий в секунду"
        float probabilityThisFrame = rate * Time.deltaTime;

        if (Random.value < probabilityThisFrame)
        {
            TriggerRandomGlitch();
        }
    }

    private void TriggerRandomGlitch()
    {
        float total =
            weightFrameDrop + weightFrameLoss + weightOverexposure +
            weightMotionBlur + weightNoiseGrain + weightCompressionGlitch;

        if (total <= 0f)
            return;

        float roll = Random.value * total;

        if ((roll -= weightFrameDrop) < 0f)
        {
            StartGlitch(FrameDropRoutine(RandomRange(frameDropDuration)));
        }
        else if ((roll -= weightFrameLoss) < 0f)
        {
            StartGlitch(FrameLossRoutine(RandomRange(frameLossDuration)));
        }
        else if ((roll -= weightOverexposure) < 0f)
        {
            StartGlitch(OverexposureRoutine(RandomRange(overexposureDuration)));
        }
        else if ((roll -= weightMotionBlur) < 0f)
        {
            StartGlitch(MotionBlurRoutine(RandomRange(motionBlurDuration)));
        }
        else if ((roll -= weightNoiseGrain) < 0f)
        {
            StartGlitch(NoiseGrainRoutine(RandomRange(noiseGrainDuration)));
        }
        else
        {
            StartGlitch(CompressionGlitchRoutine(RandomRange(compressionGlitchDuration)));
        }
    }

    private void StartGlitch(IEnumerator routine)
    {
        if (activeGlitchRoutine != null)
            StopCoroutine(activeGlitchRoutine);

        activeGlitchRoutine = StartCoroutine(routine);
    }

    // --- Отдельные типы сбоев ---

    private IEnumerator FrameDropRoutine(float duration)
    {
        // Камера на короткое время не рендерит новый кадр -> YOLO получает
        // повторяющийся (устаревший) кадр, как при "залипании" потока.
        isGlitchActive = true;

        bool hadCamera = sourceCamera != null;
        if (hadCamera)
            sourceCamera.enabled = false;

        yield return new WaitForSeconds(duration);

        if (hadCamera)
            sourceCamera.enabled = true;

        isGlitchActive = false;
    }

    private IEnumerator FrameLossRoutine(float duration)
    {
        // Кратковременная полная потеря сигнала - чёрный кадр с редкими помехами.
        isGlitchActive = true;

        if (overlay != null)
        {
            overlay.texture = null;
            overlay.color = Color.black;
        }

        yield return new WaitForSeconds(duration);

        if (overlay != null)
            overlay.color = new Color(1f, 1f, 1f, 0f);

        isGlitchActive = false;
    }

    private IEnumerator OverexposureRoutine(float duration)
    {
        // Резкая засветка (например, солнце/фара попали в объектив).
        isGlitchActive = true;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // плавно нарастает и спадает (кривая колокола) - реалистичнее, чем резкий скачок
            float t = elapsed / duration;
            float intensity = Mathf.Sin(t * Mathf.PI); // 0 -> 1 -> 0

            if (overlay != null)
            {
                overlay.texture = null;
                overlay.color = new Color(1f, 1f, 1f, intensity * 0.85f);
            }

            yield return null;
        }

        if (overlay != null)
            overlay.color = new Color(1f, 1f, 1f, 0f);

        isGlitchActive = false;
    }

    private IEnumerator MotionBlurRoutine(float duration)
    {
        // Простая имитация смаза через быстрые полупрозрачные "остаточные" кадры
        // сложенные друг на друга. Если у вас Post-Processing Stack / URP,
        // здесь лучше дёрнуть встроенный Motion Blur volume override вместо этого.
        isGlitchActive = true;

        if (overlay != null)
        {
            overlay.texture = null;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float flicker = Random.Range(0.15f, 0.35f);
                overlay.color = new Color(1f, 1f, 1f, flicker);
                yield return null;
            }

            overlay.color = new Color(1f, 1f, 1f, 0f);
        }
        else
        {
            yield return new WaitForSeconds(duration);
        }

        isGlitchActive = false;
    }

    private IEnumerator NoiseGrainRoutine(float duration)
    {
        // Шум сенсора при слабом освещении - зернистость поверх картинки.
        isGlitchActive = true;

        if (overlay != null)
            overlay.texture = noiseTexture;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            if (overlay != null)
                overlay.color = new Color(1f, 1f, 1f, Random.Range(0.08f, 0.18f));

            // "перемешиваем" шум, регенерируя текстуру время от времени
            if (Random.value < 0.1f)
                RandomizeNoiseTexture();

            yield return null;
        }

        if (overlay != null)
        {
            overlay.texture = null;
            overlay.color = new Color(1f, 1f, 1f, 0f);
        }

        isGlitchActive = false;
    }

    private IEnumerator CompressionGlitchRoutine(float duration)
    {
        // "Рассыпание" картинки блоками - как при плохом канале передачи.
        isGlitchActive = true;

        if (overlay != null)
            overlay.texture = noiseTexture;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            if (overlay != null)
            {
                RandomizeNoiseTexture();
                overlay.color = new Color(1f, 1f, 1f, Random.Range(0.4f, 0.7f));
            }

            yield return new WaitForSeconds(0.04f); // резкие рывки, не плавно
        }

        if (overlay != null)
        {
            overlay.texture = null;
            overlay.color = new Color(1f, 1f, 1f, 0f);
        }

        isGlitchActive = false;
    }

    // --- Утилиты ---

    private float RandomRange(Vector2 range)
    {
        return Random.Range(range.x, range.y);
    }

    private Texture2D GenerateNoiseTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        RandomizeNoiseTexture(tex);
        return tex;
    }

    private void RandomizeNoiseTexture()
    {
        RandomizeNoiseTexture(noiseTexture);
    }

    private void RandomizeNoiseTexture(Texture2D tex)
    {
        var pixels = new Color32[tex.width * tex.height];

        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = (byte)Random.Range(0, 256);
            // иногда добавляем "битые блоки" ярких/тёмных пятен -> похоже на compression artifact
            if (Random.value < 0.02f)
                v = Random.value < 0.5f ? (byte)0 : (byte)255;

            pixels[i] = new Color32(v, v, v, 255);
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }
}