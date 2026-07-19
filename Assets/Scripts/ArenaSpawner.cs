using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Раскладывает N копий префаба одной "арены" (робот + MazeGenerator +
/// мяч + кубик + стены) в равномерную сетку с фиксированным отступом
/// между центрами, чтобы можно было обучать сразу на 50+ аренах
/// параллельно в одной сцене (ML-Agents сам батчит всех агентов
/// с одинаковым Behavior Name).
///
/// ВАЖНО: сама арена (префаб) должна быть собрана так, чтобы ВСЁ
/// внутри неё (MazeGenerator, спавны мяча/кубика, стены) было
/// расположено относительно ЛОКАЛЬНОГО transform корня арены,
/// а не мировых координат — иначе при переносе копий в сетке
/// всё "поедет" и арены наложатся друг на друга.
/// </summary>
public class ArenaSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("Префаб одной арены целиком (робот, лабиринт, мяч, кубик, стены)")]
    public GameObject arenaPrefab;

    [Header("Сколько арен")]
    [Min(1)] public int arenaCount = 50;

    [Header("Сетка")]
    [Tooltip("Размер одной арены в метрах (только для отрисовки гизмо, на спавн не влияет)")]
    public Vector2 arenaFootprint = new Vector2(6f, 7f);

    [Tooltip("Расстояние между ЦЕНТРАМИ соседних арен по X и Z, метры")]
    public float arenaSpacing = 10f;

    [Tooltip("Число колонок в сетке. 0 = подобрать автоматически (ближе к квадрату)")]
    public int columnsOverride = 0;

    [Tooltip("Центрировать всю сетку вокруг позиции этого объекта")]
    public bool centerGrid = true;

    [Header("Когда спавнить")]
    [Tooltip("Спавнить автоматически при старте сцены")]
    public bool spawnOnAwake = true;

    private readonly List<GameObject> spawnedArenas = new List<GameObject>();

    void Awake()
    {
        if (spawnOnAwake)
            SpawnArenas();
    }

    [ContextMenu("Spawn Arenas Now")]
    public void SpawnArenas()
    {
        if (arenaPrefab == null)
        {
            Debug.LogError("ArenaSpawner: arenaPrefab не назначен!");
            return;
        }

        ClearArenas();

        int columns = columnsOverride > 0
            ? columnsOverride
            : Mathf.CeilToInt(Mathf.Sqrt(arenaCount));

        int rows = Mathf.CeilToInt(arenaCount / (float)columns);

        // Смещение, чтобы вся сетка была отцентрирована вокруг transform.position
        Vector3 gridOffset = Vector3.zero;
        if (centerGrid)
        {
            float gridWidth = (columns - 1) * arenaSpacing;
            float gridDepth = (rows - 1) * arenaSpacing;
            gridOffset = new Vector3(-gridWidth * 0.5f, 0f, -gridDepth * 0.5f);
        }

        for (int i = 0; i < arenaCount; i++)
        {
            int col = i % columns;
            int row = i / columns;

            Vector3 localPos = new Vector3(col * arenaSpacing, 0f, row * arenaSpacing) + gridOffset;
            Vector3 worldPos = transform.position + localPos;

            GameObject arena = Instantiate(arenaPrefab, worldPos, Quaternion.identity, transform);
            arena.name = $"Arena_{i:D3}";

            spawnedArenas.Add(arena);
        }

        Debug.Log($"ArenaSpawner: заспавнено {spawnedArenas.Count} арен ({columns}x{rows}), шаг {arenaSpacing} м.");
    }

    [ContextMenu("Clear Arenas")]
    public void ClearArenas()
    {
        foreach (var arena in spawnedArenas)
        {
            if (arena != null)
            {
                if (Application.isPlaying)
                    Destroy(arena);
                else
                    DestroyImmediate(arena);
            }
        }

        spawnedArenas.Clear();
    }

    void OnDrawGizmosSelected()
    {
        int columns = columnsOverride > 0
            ? columnsOverride
            : Mathf.CeilToInt(Mathf.Sqrt(arenaCount));

        int rows = Mathf.CeilToInt(arenaCount / (float)columns);

        Vector3 gridOffset = Vector3.zero;
        if (centerGrid)
        {
            float gridWidth = (columns - 1) * arenaSpacing;
            float gridDepth = (rows - 1) * arenaSpacing;
            gridOffset = new Vector3(-gridWidth * 0.5f, 0f, -gridDepth * 0.5f);
        }

        Gizmos.color = Color.cyan;

        for (int i = 0; i < arenaCount; i++)
        {
            int col = i % columns;
            int row = i / columns;

            Vector3 localPos = new Vector3(col * arenaSpacing, 0f, row * arenaSpacing) + gridOffset;
            Vector3 center = transform.position + localPos;

            Gizmos.DrawWireCube(center, new Vector3(arenaFootprint.x, 0.1f, arenaFootprint.y));
        }
    }
}
