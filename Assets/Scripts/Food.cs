using Unity.Mathematics;
using UnityEngine;

public class Food : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        const float noiseSpeed = 0.5f;
        var velocity = new Vector3(
            -1f + 2f * Mathf.PerlinNoise1D(Time.time * noiseSpeed + 0.571f),
            -1f + 2f * Mathf.PerlinNoise1D(Time.time * noiseSpeed + 5.113f),
            -1f + 2f * Mathf.PerlinNoise1D(Time.time * noiseSpeed + 6.733f)
        );
        transform.position += velocity * Time.deltaTime;
    }
}
