using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ObstacleController : MonoBehaviour
{
    [Header("Noise (in meters, applied on top of prefab's current scale)")]
    public float noiseRange = 0.01f; // ±10мм

    [Header("Density")]
    public float density = 80f; // кг/м³, картон

    private Rigidbody rb;
    private Vector3 baseSize;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        baseSize = transform.localScale; // берём то, что уже стоит в префабе
    }

    public void RandomizeSizeAndMass()
    {
        float x = baseSize.x + Random.Range(-noiseRange, noiseRange);
        float y = baseSize.y + Random.Range(-noiseRange, noiseRange);
        float z = baseSize.z + Random.Range(-noiseRange, noiseRange);

        transform.localScale = new Vector3(x, y, z);

        float volume = x * y * z;
        rb.mass = volume * density;
    }
}