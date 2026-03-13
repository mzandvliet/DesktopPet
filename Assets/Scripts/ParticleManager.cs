
using Unity.Mathematics;
using UnityEngine;
using Rng = Unity.Mathematics.Random;

public class ParticleManager : MonoBehaviour
{
    [SerializeField] private ParticleSystem _particles;

    public ParticleSystem Triangles
    {
        get => _particles;
    }

    private Rng _rng;

    private void Awake()
    {
        _rng = new Rng(1023589);
    }

    public void Splash(Vector3 position, Vector3 normal)
    {
        var emit = new ParticleSystem.EmitParams();
        emit.position = position + normal * 0.1f;
        emit.startLifetime = _rng.NextFloat(0.5f, 1.5f);
        emit.startSize = _rng.NextFloat(0.25f, 1f) * 0.15f;

        // var rot = Quaternion.Euler(new Vector3(_rng.NextFloat(-45f, 45f), 0, 0)) * Quaternion.Euler(0f, _rng.NextFloat(0, 360), 0);
        emit.velocity = normal * 5;//_rng.NextFloat(4f, 6f);
        _particles.Emit(emit, 1);
    }

    public void SplashMany(Vector3 position, Vector3 normal)
    {
        var emit = new ParticleSystem.EmitParams();
        emit.position = position;
        emit.startLifetime = 0.6f;

        for (int p = 0; p < 10; p++)
        {
            var rot = Quaternion.Euler(new Vector3(_rng.NextFloat(-45f, 45f), 0, 0)) * Quaternion.Euler(0f, _rng.NextFloat(0, 360), 0);
            emit.velocity = rot * normal * _rng.NextFloat(4f, 6f);
            _particles.Emit(emit, 1);
        }
    }
}