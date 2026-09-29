using UnityEngine;

public class Rotate : MonoBehaviour
{
    [Range(0, 100f)]
    public float rotateSpeed = 20f;

    [Range(0.01f, 2f)]
    public float changeSpeed = 0.3f;

    private float noiseOffsetX;
    private float noiseOffsetY;
    private float noiseOffsetZ;

    void Start()
    {
        noiseOffsetX = Random.Range(0f, 1000f);
        noiseOffsetY = Random.Range(0f, 1000f);
        noiseOffsetZ = Random.Range(0f, 1000f);
    }

    void Update()
    {
        float t = Time.time * changeSpeed;

        Vector3 axis = new Vector3(
            Mathf.PerlinNoise(t + noiseOffsetX, 0f) - 0.5f,
            Mathf.PerlinNoise(t + noiseOffsetY, 0f) - 0.5f,
            Mathf.PerlinNoise(t + noiseOffsetZ, 0f) - 0.5f
        ).normalized;

        transform.Rotate(axis * (rotateSpeed * Time.deltaTime));
    }
}