using Unity.Mathematics;
using UnityEngine;

public class Food : MonoBehaviour
{
    private Transform _transform;
    private Vector3 _velocity;

    void Start()
    {
        _transform = gameObject.GetComponent<Transform>();
        _velocity = RngManager.Shared.NextFloat3Direction() * 0.5f;
    }

    // Update is called once per frame
    void Update()
    {
        const float velocityScale = .66f;
        const float noiseSpaceScale = 0.5f;
        const float noiseSpeed = 1f;

        var waterVelocity = new Vector3(
            -1f + 2f * Mathf.PerlinNoise1D(_transform.position.x * noiseSpaceScale + Time.time * noiseSpeed + 0.571f),
            -1f + 2f * Mathf.PerlinNoise1D(_transform.position.y * noiseSpaceScale + Time.time * noiseSpeed + 5.113f),
            -1f + 2f * Mathf.PerlinNoise1D(_transform.position.z * noiseSpaceScale + Time.time * noiseSpeed + 6.733f)
        ) * velocityScale;

        _velocity -= _velocity * 0.1f * Time.deltaTime;

        _transform.position += (_velocity + waterVelocity) * Time.deltaTime;
    }
}
