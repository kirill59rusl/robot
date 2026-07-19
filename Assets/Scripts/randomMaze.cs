using UnityEngine;
using System.Collections.Generic;

public class MazeGenerator : MonoBehaviour
{
    [Header("Настройки лабиринта")]
    [SerializeField] private GameObject groundPlanePrefab;
    [SerializeField] private GameObject obstaclePrefab;
    [SerializeField] private GameObject ballPrefab;
    [SerializeField] private GameObject startCubePrefab;
    
    
    [Header("Параметры генерации")]
    [SerializeField] private bool randomizeObstacleCount = true;

    [SerializeField] private int minObstacleCount = 15;
    [SerializeField] private int maxObstacleCount = 40;

    [SerializeField] private int obstacleCount = 30; // используется, если randomization выключен
    [SerializeField] private float minDistanceFromStart = 0.9f;
    [SerializeField] private float maxDistanceFromBallToStart = 10f;
    [SerializeField] private float minDistanceBetweenObstacles = 0.6f;
    [SerializeField] private float minDistanceFromEdge = 0.2f;
    [SerializeField] private float ballRadius = 0.04f;
    
    [Header("Параметры шарика")]
    [SerializeField] private float minDistanceFromBallToObstacle = 0.3f; // Минимальное расстояние от шарика до препятствий
    [SerializeField] private float minDistanceFromBallToStart = 0.4f; // Минимальное расстояние от шарика до старта
    
    
    [Header("Размеры (если не заданы в префабе)")]
    [SerializeField] private Vector2 groundSize = new Vector2(0.7f, 0.6f);
    [SerializeField] private Vector3 obstacleSize = new Vector3(0.36f, 0.24f, 0.14f);
    [SerializeField] private float ballScale = 0.08f;
    [SerializeField] private float cubeScale = 0.08f;
    
    // Приватные переменные
    private int currentObstacleCount;
    private GameObject groundPlane;
    private Vector3 groundSizeWorld;
    private List<Vector3> obstaclePositions = new List<Vector3>();
    private List<Vector3> availablePositions = new List<Vector3>();
    private GameObject ballObject;
    private GameObject startCube;
    private Vector3 startPosition;
    private Transform obstaclesParent;
    private Vector3 startCubePosition;

    private void Start()
    {
        GenerateMaze();
    }
    
    public void GenerateMaze()
    {
        ClearMaze();
        if (randomizeObstacleCount)
        {
            currentObstacleCount = Random.Range(minObstacleCount, maxObstacleCount + 1);
        }
        else
        {
            currentObstacleCount = obstacleCount;
        }
        CreateGround();
        
        MeshRenderer groundRenderer = groundPlane.GetComponent<MeshRenderer>();
        groundSizeWorld = groundRenderer.bounds.size;
        
        obstaclesParent = new GameObject("Obstacles").transform;
        obstaclesParent.parent = transform;
        minDistanceBetweenObstacles *= Random.Range(0.8f, 1.5f);
        availablePositions = GetAvailablePositions();
        
        if (availablePositions.Count < 5)
        {
            Debug.LogError("Слишком мало места для генерации! Уменьшите количество препятствий или размеры объектов.");
            return;
        }
        
        SetRandomStartPosition();
        
        GenerateObstacles();
        CreateBall();
        
        
    }
    
    private void CreateGround()
    {
        if (groundPlanePrefab != null)
        {
            // ВАЖНО: используем transform.position арены, а не Vector3.zero -
            // иначе земля всех арен окажется в одной мировой точке (0,0,0)
            // независимо от того, куда расставил арены ArenaSpawner.
            groundPlane = Instantiate(groundPlanePrefab, transform.position, Quaternion.identity, transform);
            groundPlane.name = "Ground";
            
            MeshRenderer renderer = groundPlane.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.bounds.size.x == 10f)
            {
                groundPlane.transform.localScale = new Vector3(
                    groundSize.x / 10f,
                    1f,
                    groundSize.y / 10f
                );
            }
        }
        else
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.SetParent(transform); // без этого ClearMaze() не найдёт и не удалит её при RegenerateMaze()
            plane.transform.position = transform.position; // мировая позиция самой арены, не (0,0,0)
            plane.transform.localScale = new Vector3(
                groundSize.x / 10f,
                1f,
                groundSize.y / 10f
            );
            plane.name = "Ground";
            groundPlane = plane;
        }
    }
    
    private void SetRandomStartPosition()
{
    int randomIndex = Random.Range(0, availablePositions.Count);

    // Позиция спавна робота
    startPosition = availablePositions[randomIndex];
    startPosition.y = cubeScale / 2;

    availablePositions.RemoveAt(randomIndex);

    float minOffset = 0.4f;   // минимальное расстояние от робота
    float maxOffset = 0.8f;   // максимальное расстояние

    float halfWidth = groundSizeWorld.x / 2 - minDistanceFromEdge;
    float halfDepth = groundSizeWorld.z / 2 - minDistanceFromEdge;

    const int maxAttempts = 50;

    startCubePosition = startPosition;

    for (int i = 0; i < maxAttempts; i++)
    {
        // Случайное направление
        Vector2 dir = Random.insideUnitCircle.normalized;

        // Случайное расстояние
        float distance = Random.Range(minOffset, maxOffset);

        Vector3 candidate = startPosition + new Vector3(dir.x, 0f, dir.y) * distance;

        // Проверяем, что точка находится в пределах поля - ОТНОСИТЕЛЬНО ЦЕНТРА
        // ЭТОЙ АРЕНЫ (transform.position), а не абсолютных мировых координат.
        // Иначе при расстановке нескольких арен в сетке (не по центру мира)
        // проверка ломается для всех арен, кроме той, что стоит в (0,0,0).
        float localX = candidate.x - transform.position.x;
        float localZ = candidate.z - transform.position.z;

        if (Mathf.Abs(localX) > halfWidth ||
            Mathf.Abs(localZ) > halfDepth)
            continue;

        startCubePosition = candidate;
        break;
    }

    startCubePosition.y = cubeScale / 2;

    startCube = Instantiate(
        startCubePrefab,
        startCubePosition,
        Quaternion.identity,
        transform);

    startCube.transform.localScale = Vector3.one * cubeScale;
    startCube.name = "StartCube";
    startCube.tag = "Goal";
}
    
    
    private void GenerateObstacles()
    {
        int attempts = 0;
        int maxAttempts = currentObstacleCount * 100;
        int placedCount = 0;
        
        List<Vector3> availableForObstacles = new List<Vector3>(availablePositions);
        
        while (placedCount < currentObstacleCount && attempts < maxAttempts && availableForObstacles.Count > 0)
        {
            attempts++;
            
            int randomIndex = Random.Range(0, availableForObstacles.Count);
            Vector3 position = availableForObstacles[randomIndex];
            position.y = obstacleSize.y / 2;
            
            // Проверка расстояния до старта
            float distanceToStart = Vector3.Distance(
                new Vector3(position.x, 0, position.z),
                new Vector3(startPosition.x, 0, startPosition.z)
            );
            
            if (distanceToStart < minDistanceFromStart)
            {
                availableForObstacles.RemoveAt(randomIndex);
                continue;
            }
            
            
            
            // Проверка расстояния до других препятствий
            bool tooClose = false;
            foreach (Vector3 existingPos in obstaclePositions)
            {
                float dist = Vector3.Distance(
                    new Vector3(position.x, 0, position.z),
                    new Vector3(existingPos.x, 0, existingPos.z)
                );
                
                if (dist < minDistanceBetweenObstacles)
                {
                    tooClose = true;
                    break;
                }
            }
            
            if (tooClose)
            {
                availableForObstacles.RemoveAt(randomIndex);
                continue;
            }
            
            // Создаем препятствие
            Quaternion randomRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            GameObject obstacle = Instantiate(obstaclePrefab, position, randomRotation, obstaclesParent);
            obstacle.transform.localScale = obstacleSize;
            obstacle.name = $"Obstacle_{placedCount}";
            obstacle.tag = "Obstacle";
            var obstacleController = obstacle.GetComponent<ObstacleController>();
            if (obstacleController != null)
            {
                obstacleController.RandomizeSizeAndMass();
            }
            else
            {

                obstacle.transform.localScale = obstacleSize; // fallback, если контроллера нет
              
                Debug.LogWarning($"MassRandomizer отсутствует на {obstacle.name}!");
            }

            obstaclePositions.Add(position);
            availableForObstacles.RemoveAt(randomIndex);
            availablePositions.Remove(position);
            
            placedCount++;
        }
        
        if (placedCount < currentObstacleCount)
        {
            Debug.LogWarning($"Удалось разместить только {placedCount} препятствий из {obstacleCount} запрошенных");
        }
    }
    
    private void CreateBall()
    {
        if (availablePositions.Count == 0)
        {
            Debug.LogWarning("Не найдено доступных позиций для мячика!");
            return;
        }
        
        // Фильтруем позиции для мячика с учетом всех минимальных расстояний
        List<Vector3> validBallPositions = new List<Vector3>();
        
        foreach (Vector3 pos in availablePositions)
        {
            bool isValid = true;
            
            // Проверка расстояния до старта
            float distToStart = Vector3.Distance(
                new Vector3(pos.x, 0, pos.z),
                new Vector3(startPosition.x, 0, startPosition.z)
            );
            
            if (distToStart < minDistanceFromBallToStart ||
                distToStart > maxDistanceFromBallToStart)
            {
                isValid = false;
                continue;
            }
            
            // Проверка расстояния до препятствий
            foreach (Vector3 obstaclePos in obstaclePositions)
            {
                float distToObstacle = Vector3.Distance(
                    new Vector3(pos.x, 0, pos.z),
                    new Vector3(obstaclePos.x, 0, obstaclePos.z)
                );
                
                if (distToObstacle < minDistanceFromBallToObstacle)
                {
                    isValid = false;
                    break;
                }
            }
            
            if (!isValid) continue;
            
            
            
            if (isValid)
            {
                validBallPositions.Add(pos);
            }
        }
        
        if (validBallPositions.Count == 0)
        {
            Debug.LogWarning("Не найдено подходящих позиций для мячика с учетом всех минимальных расстояний! Использую случайную из доступных.");
            
            // Если нет подходящих позиций, берем случайную из доступных
            Vector3 fallbackPos = availablePositions[Random.Range(0, availablePositions.Count)];
            fallbackPos.y = ballScale / 2;
            
            ballObject = Instantiate(ballPrefab, fallbackPos, Quaternion.identity, transform);
            ballObject.transform.localScale = Vector3.one * ballScale;
            ballObject.name = "Ball";
            
            
            return;
        }
        
        // Выбираем случайную позицию из отфильтрованных
        Vector3 ballPosition = validBallPositions[Random.Range(0, validBallPositions.Count)];
        ballPosition.y = ballScale / 2;
        
        ballObject = Instantiate(ballPrefab, ballPosition, Quaternion.identity, transform);
        ballObject.transform.localScale = Vector3.one * ballScale;
        ballObject.name = "Ball";
        ballObject.tag = "TargetBall";
        
        
    }
    
    private List<Vector3> GetAvailablePositions()
    {
        List<Vector3> positions = new List<Vector3>();
        
        float step = 0.12f;
        float halfWidth = groundSizeWorld.x / 2 - minDistanceFromEdge;
        float halfDepth = groundSizeWorld.z / 2 - minDistanceFromEdge;
        
        float minObjectSize = Mathf.Max(obstacleSize.x, obstacleSize.z, ballScale) / 2 + 0.05f;
        
        for (float x = -halfWidth; x <= halfWidth; x += step)
        {
            for (float z = -halfDepth; z <= halfDepth; z += step)
            {
                Vector3 checkPos = new Vector3(
                    groundPlane.transform.position.x + x,
                    0,
                    groundPlane.transform.position.z + z
                );
                
                if (Mathf.Abs(x) > halfWidth - minObjectSize || Mathf.Abs(z) > halfDepth - minObjectSize)
                    continue;
                
                bool isBlocked = false;
                foreach (Vector3 obstaclePos in obstaclePositions)
                {
                    float dist = Vector3.Distance(
                        new Vector3(checkPos.x, 0, checkPos.z),
                        new Vector3(obstaclePos.x, 0, obstaclePos.z)
                    );
                    
                    if (dist < minDistanceBetweenObstacles * 0.5f)
                    {
                        isBlocked = true;
                        break;
                    }
                }
                
                if (!isBlocked)
                {
                    positions.Add(checkPos);
                }
            }
        }
        
        return positions;
    }
    
    // Метод для проверки, безопасна ли позиция для мячика
    public bool IsPositionSafeForBall(Vector3 position)
    {
        // Проверка расстояния до старта
        if (Vector3.Distance(
            new Vector3(position.x, 0, position.z),
            new Vector3(startPosition.x, 0, startPosition.z)
        ) < minDistanceFromBallToStart)
        {
            return false;
        }
        
        // Проверка расстояния до препятствий
        foreach (Vector3 obstaclePos in obstaclePositions)
        {
            float distToStart = Vector3.Distance(
                new Vector3(position.x, 0, position.z),
                new Vector3(startPosition.x, 0, startPosition.z)
            );

            if (distToStart < minDistanceFromBallToStart ||
                distToStart > maxDistanceFromBallToStart)
            {
                return false;
            }
        }
        
        
        return true;
    }
    
    // Метод для получения всех безопасных позиций для мячика
    public List<Vector3> GetSafePositionsForBall()
    {
        List<Vector3> safePositions = new List<Vector3>();
        
        foreach (Vector3 pos in availablePositions)
        {
            if (IsPositionSafeForBall(pos))
            {
                safePositions.Add(pos);
            }
        }
        
        return safePositions;
    }
    
    public Transform GetGoal()
{
    return startCube.transform;
}
    public Transform GetBall(){
        return ballObject.transform;
    }

    private void ClearMaze()
    {
        foreach (Transform child in transform)
        {
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
        if (ballObject != null)
        {
            if (Application.isPlaying)
                Destroy(ballObject);
            else
                DestroyImmediate(ballObject);
        
            ballObject = null;
        }
        obstaclePositions.Clear();
        availablePositions.Clear();
        
        groundPlane = null;
        ballObject = null;
        startCube = null;
        obstaclesParent = null;
        startPosition = Vector3.zero;
    }
    
    public void RegenerateMaze()
    {
        GenerateMaze();
    }
    
    public Vector3 GetStartPosition()
    {
        return startPosition;
    }
    
    public Vector3 GetBallPosition()
    {
        if (ballObject != null)
            return ballObject.transform.position;
        return Vector3.zero;
    }
    
    public bool IsBallAtStart()
    {
        if (ballObject == null || startCube == null) return false;
        
        float distance = Vector3.Distance(
            new Vector3(ballObject.transform.position.x, 0, ballObject.transform.position.z),
            new Vector3(startCubePosition.x, 0, startCubePosition.z)
        );
        
        return distance < 0.15f;
    }
    
    public int GetObstacleCount()
    {
        return obstaclePositions.Count;
    }
    
    private void OnDrawGizmosSelected()
    {
        if (groundPlane == null)
        {
            Gizmos.color = Color.gray;
            Vector3 center = transform.position;
            Vector3 size = new Vector3(groundSize.x, 0.01f, groundSize.y);
            Gizmos.DrawWireCube(center, size);
            return;
        }
        
        if (startCube != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(startPosition, Vector3.one * cubeScale * 1.5f);
            
            // Зона минимального расстояния от старта для мячика
            Gizmos.color = new Color(0, 0, 1, 0.15f);
            Gizmos.DrawWireSphere(
                new Vector3(startPosition.x, 0, startPosition.z),
                minDistanceFromBallToStart
            );
        }
        
        // Зона минимального расстояния от препятствий для мячика
        if (obstaclePositions.Count > 0)
        {
            Gizmos.color = new Color(1, 0, 0, 0.1f);
            foreach (Vector3 pos in obstaclePositions)
            {
                Gizmos.DrawWireSphere(
                    new Vector3(pos.x, 0, pos.z),
                    minDistanceFromBallToObstacle
                );
                Gizmos.color = new Color(0, 1, 1, 0.15f);
                Gizmos.DrawWireSphere(
                    new Vector3(startPosition.x, 0, startPosition.z),
                    maxDistanceFromBallToStart
                );
            }
        }
        
        
        
        // Безопасные позиции для мячика
        if (availablePositions.Count > 0)
        {
            List<Vector3> safePositions = GetSafePositionsForBall();
            
            foreach (Vector3 pos in availablePositions)
            {
                if (safePositions.Contains(pos))
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(pos, ballRadius);
                }
                else
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(pos, ballRadius * 0.5f);
                }
            }
        }
        
        if (groundPlane != null)
        {
            Gizmos.color = new Color(1, 1, 0, 0.3f);
            MeshRenderer renderer = groundPlane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Vector3 size = renderer.bounds.size;
                Gizmos.DrawWireCube(groundPlane.transform.position, size);
            }
        }
    }
}