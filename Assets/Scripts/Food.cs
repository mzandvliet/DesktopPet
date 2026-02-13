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

        Destroy(gameObject, 30f);
    }

    // Update is called once per frame
    void Update()
    {
        const float velocityScale = .2f;
        const float noiseSpaceScale = 1f;
        const float noiseSpeed = 0.25f;

        var wanderVelocity = new Vector3(
            -1f + 2f * Mathf.PerlinNoise1D(math.fmod(_transform.position.x * noiseSpaceScale + Time.time * noiseSpeed + 0.571f, 1f)),
            -1f + 2f * Mathf.PerlinNoise1D(math.fmod(_transform.position.y * noiseSpaceScale + Time.time * noiseSpeed + 5.113f, 1f)),
            -1f + 2f * Mathf.PerlinNoise1D(math.fmod(_transform.position.z * noiseSpaceScale + Time.time * noiseSpeed + 6.733f, 1f))
        ) * velocityScale;

        _velocity += wanderVelocity;
    
        if (Physics.Raycast(_transform.position, _velocity, out RaycastHit hit, 1f))
        {
            _velocity -= Vector3.Project(_velocity, hit.normal);
        }

        _velocity -= _velocity * 0.33f * Time.deltaTime;

        _transform.position += _velocity * Time.deltaTime;
    }
}
