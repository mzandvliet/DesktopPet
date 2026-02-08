using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Shapes;

/* 
    Goal:
    Curious-but-anxious fish
    They swim in the background, sometimes dare to explore the foreground
    They're really responsive to your mouse
    You can feed them
    This will gradually change their behaviour
    You can whisk or chase them away by wooshing your cursor by them with a little aggression

    fish emotions and impulses:
    - hungry
    - curious
    - belonging vs. individualism

    Todo:
    - Define a box area with soft boundaries, align with screen / aspect ratio
    - Spatial hashing
    - Instanced rendering for skinned/animated meshes
    - Weighting scheme for all the various behavioural factors (heh, SoftMax might work)

    Later, use ECS like so:

    https://software.intel.com/en-us/articles/get-started-with-the-unity-entity-component-system-ecs-c-sharp-job-system-and-burst-compiler
    https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/Documentation/content/ecs_in_detail.md#automatic-job-dependency-management-jobcomponentsystem
    Once interop with classic unity is introduce shit goes complex quite fast
 */

[System.Serializable]
public struct Boid
{
    public float3 Position;
    public float3 Velocity;
    public float Individuality;
}

public class Boids : ImmediateModeShapeDrawer
{
    [SerializeField] private LayerMask _theaterLayer;
    [SerializeField] private float _mouseAvoidDistance = 5f;
    [SerializeField] private float _avoidanceMin = 0.05f;
    [SerializeField] private float _avoidLerpSpeed = 0.1f;
    [SerializeField] float _maxSpeed = 3f;
    [SerializeField] float _sphereRadius = 20f;
    [SerializeField] float _neighbourViewRange = 4f;
    [SerializeField] float _neighbourSeparateRange = 2f;
    [SerializeField] int _numNearestNeighbours = 7; // init time only
    [SerializeField] private float _coherence = 0.5f;
    [SerializeField] private float _alignment = 0.5f;
    [SerializeField] private float _separation = 0.1f;


    private NativeArray<Boid> _boids;
    private Unity.Mathematics.Random _rng;

    private Collider[] _collidersNearby;
    private Ray _mouseRay = new Ray(new Vector3(-9999, -9999, -9999), Vector3.down); // ray length must never be zero
    private Vector3 _mouseVelocity;

    private const int NUM_BOIDS = 256;

    private void Awake()
    {
        _boids = new NativeArray<Boid>(NUM_BOIDS, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _collidersNearby = new Collider[8];
        _rng = new Unity.Mathematics.Random(1234);

        InitBoids();
    }

    private void InitBoids()
    {
        for (int i = 0; i < _boids.Length; i++)
        {
            var b = new Boid();
            b.Position = _rng.NextFloat3() * 20f;
            b.Position.z = 0;
            b.Velocity = _rng.NextFloat3Direction() * 5f;
            b.Velocity.z = 0;
            _boids[i] = b;
        }
    }

    private void OnDestroy()
    {
        _boids.Dispose();
    }

    private void Update()
    {
        UpdateBoids();
    }

    public override void DrawShapes(Camera cam)
    {
        using (Draw.Command(cam)) // UnityEngine.Rendering.Universal.RenderPassEvent.BeforeRendering
        {
            Draw.ThicknessSpace = ThicknessSpace.Pixels;
            Draw.RadiusSpace = ThicknessSpace.Meters;
            Draw.Thickness = 1f;
            Draw.BlendMode = ShapesBlendMode.Opaque;

            for (int i = 0; i < _boids.Length; i++)
            {
                var b = _boids[i];

                Draw.Color = Color.Lerp(Color.gray, Color.darkCyan, b.Individuality);
                Draw.Line(b.Position, b.Position + b.Velocity * 0.05f);
                Draw.Sphere(b.Position, 0.05f);
            }
        }
    }

    public void SetMouseData(Ray ray, Vector3 velocity)
    {
        _mouseRay = ray;
        _mouseVelocity = velocity;
    }

    // Note: this is of course a very naive implementation...
    private void UpdateBoids()
    {
        float mouseSpeed = math.length(_mouseVelocity);
        float mouseAvoidance = math.lerp(_avoidanceMin, 1f, math.saturate(mouseSpeed / 1f));

        float sphereRadiusOneOver = 1f / _sphereRadius;

        var nearest = new NativeList<int>(_numNearestNeighbours, Allocator.Temp);
        for (int bi = 0; bi < _boids.Length; bi++)
        {
            var b = _boids[bi];

            const float velocityScale = 0.2f;
            const float noiseSpaceScale = 0.5f;
            const float noiseSpeed = 0.1f;

            b.Individuality = Mathf.PerlinNoise1D(Time.time * noiseSpeed * 0.5f + 0.571f * bi) * 0.5f;
            // b.Individuality *= b.Individuality;

            /* Swim in random direction */

            var individualVelocity = new float3(
                -1f + 2f * Mathf.PerlinNoise1D(b.Position.x * noiseSpaceScale + Time.time * noiseSpeed + 0.571f),
                -1f + 2f * Mathf.PerlinNoise1D(b.Position.y * noiseSpaceScale + Time.time * noiseSpeed + 5.113f),
                -1f + 2f * Mathf.PerlinNoise1D(b.Position.z * noiseSpaceScale + Time.time * noiseSpeed + 6.733f)
            ) * velocityScale;
            b.Velocity += individualVelocity * b.Individuality;

            /* Swim towards things of interest */

            int numColliders = Physics.OverlapSphereNonAlloc(b.Position, 10, _collidersNearby, _theaterLayer.value);

            for (int c = 0; c < numColliders; c++)
            {
                var character = _collidersNearby[c].transform.parent?.gameObject.GetComponent<Character>();
                if (character != null)
                {
                    var delta = (float3)character.Transform.position - b.Position;
                    b.Velocity = math.lerp(b.Velocity, math.normalize(delta) * _maxSpeed * 0.5f, b.Individuality * Time.deltaTime);
                }

                var food = _collidersNearby[c].gameObject.GetComponent<Food>();
                if (food != null)
                {
                    var delta = (float3)food.transform.position - b.Position;
                    b.Velocity = math.lerp(b.Velocity, math.normalize(delta) * _maxSpeed, b.Individuality * Time.deltaTime);
                }
            }

            /* Avoid mouse */

            var posOnMouseRay = ProjectPointOntoRay(b.Position, _mouseRay);
            var mouseRayDelta = posOnMouseRay - b.Position;
            var rayDist = math.length(mouseRayDelta);
            var avoidVelocity = math.normalize(mouseRayDelta) * -_maxSpeed;
            var avoidWeight = math.saturate((_mouseAvoidDistance - rayDist) / _mouseAvoidDistance) * mouseAvoidance;
            b.Velocity = Vector3.Slerp(b.Velocity, avoidVelocity, avoidWeight * _avoidLerpSpeed);


            /* Stay within a sphere */

            var sphereCenterVelocity = math.normalize(-b.Position) * _maxSpeed;
            b.Velocity = Vector3.Slerp(b.Velocity, sphereCenterVelocity, sphereRadiusOneOver * math.length(b.Position) * Time.deltaTime);

            /* Align with neighbours */

            GetNearest(bi, _neighbourViewRange, _boids, nearest);
            for (int j = 0; j < nearest.Length; j++)
            {
                var neighbour = _boids[nearest[j]];
                float weight = (_neighbourViewRange - math.distance(b.Position, neighbour.Position)) / _neighbourViewRange;
                weight *= weight;
                weight *= 1f - b.Individuality;

                // Align velocity to neighbour
                b.Velocity = Vector3.Slerp(b.Velocity, neighbour.Velocity, weight * Time.deltaTime);
                // Maintain separation from neighbour (todo: gravitate towards ideal separation)
                var separationVelocity = math.normalize(b.Position - neighbour.Position) * _maxSpeed;
                b.Velocity = Vector3.Slerp(b.Velocity, separationVelocity, weight * weight * _separation);
            }

            /* Integrate */

            b.Position += b.Velocity * Time.deltaTime;
            _boids[bi] = b;
        }
        nearest.Dispose();
    }

    private static float3 ProjectPointOntoRay(Vector3 p, Ray ray)
    {
        var ap = p - ray.origin;
        var ab = ray.direction;
        return ray.origin + math.dot(ap, ab) / math.dot(ab, ab) * ab;
    }

    private static void GetNearest(int boidId, float distance, NativeSlice<Boid> boids, NativeList<int> nearest)
    {
        nearest.Clear();

        var position = boids[boidId].Position;

        float distSq = distance * distance;
        for (int i = 0; i < boids.Length; i++)
        {
            if (i == boidId)
            {
                continue;
            }

            if (math.distancesq(position, boids[i].Position) < distSq)
            {
                nearest.Add(i);
            }

            if (nearest.Length == nearest.Capacity)
            {
                return;
            }
        }
    }
}
