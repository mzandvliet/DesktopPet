using UnityEngine;
using Obi;
using UnityEngine.InputSystem;
using Shapes;
using Unity.Mathematics;
using System.Collections.Generic;
using System.IO;
using Rng = Unity.Mathematics.Random;
using System.Collections;

public class SoftbodySlimeApplication : ImmediateModeShapeDrawer
{
    [SerializeField] private Camera _camera;
    [SerializeField] private ObiSoftbody _slimeBody;
    [SerializeField] private GameObject _slimeBubblePrefab;
    [SerializeField] private int _numBubbles = 7;

    [SerializeField] private Material _slimeMaterial;

    [SerializeField] private float _minStickiness = 0.1f;
    [SerializeField] private float _maxStickiness = 8f;

    public ObiSolver solver;
    int filter;
    int queryIndex;
    
    Ray _ray;
    QueryResult _rayResult = new QueryResult { distanceAlongRay = float.MaxValue, simplexIndex = -1, queryIndex = -1 };
    QueryResult _dragResult = new QueryResult { distanceAlongRay = float.MaxValue, simplexIndex = -1, queryIndex = -1 };

    private Vector3 _centerOfMass;
    private Matrix4x4 _faceAnchor;

    private Rng _rng;

    private List<(Transform, int)> _slimeBubbles;

    private struct ContactPatch
    {
        public Vector3 position;
        public Vector3 normal;
        public float weight;
    }
    private List<ContactPatch> _patches = new List<ContactPatch>();

    private Texture2D _vertexRestPositions;

    private double _lastBlinkTime = -1;
    private float _blinkDuration = 3;

    private void Start()
    {
        _rng = new Rng(12345);

        filter = ObiUtils.MakeFilter(ObiUtils.CollideWithEverything, 0);

        _slimeBubbles = new List<(Transform, int)>(); // Todo: render these bubbles without using game objects
        for (int b = 0; b < _numBubbles; b++)
        {
            var solverIdx = _rng.NextInt(0, _slimeBody.solverIndices.count);
            var meshObj = GameObject.Instantiate(_slimeBubblePrefab);
            meshObj.transform.localScale = Vector3.one * _rng.NextFloat(0.1f, 0.4f);
            _slimeBubbles.Add((meshObj.transform, solverIdx));
        }

        var mesh = _slimeBody.GetComponentInChildren<SkinnedMeshRenderer>()?.sharedMesh;
        if (mesh != null)
        {
            var vertexPositions = new float4[mesh.vertexCount];
            for (int v = 0; v < mesh.vertexCount; v++)
            {
                vertexPositions[v] = new float4((float3)mesh.vertices[v], 1);
            }

            _vertexRestPositions = new Texture2D(_slimeBody.solverIndices.count, 1, TextureFormat.RGBAFloat, false)
            {
                filterMode = FilterMode.Point
            };
            _vertexRestPositions.SetPixelData(vertexPositions, 0, 0);
            _vertexRestPositions.Apply();
            _slimeMaterial.SetTexture("_VertexRestPositions", _vertexRestPositions);
        }
    }

    public override void OnEnable()
    {
        base.OnEnable();

        solver.OnSpatialQueryResults += Solver_OnSpatialQueryResults;
        solver.OnSimulationStart += Solver_OnSimulate;
        solver.OnCollision += Solver_OnCollision;
    }

    public override void OnDisable()
    {
        base.OnDisable();

        solver.OnSpatialQueryResults -= Solver_OnSpatialQueryResults;
        solver.OnSimulationStart -= Solver_OnSimulate;
        solver.OnCollision -= Solver_OnCollision;
    }

    private void Solver_OnSimulate(ObiSolver s, float simulatedTime, float substepTime)
    {
        // perform a raycast, check if it hit anything:
        _ray = _camera.ScreenPointToRay(new Vector3(Mouse.current.position.value.x, Mouse.current.position.value.y, 0.01f));
        queryIndex = solver.EnqueueRaycast(_ray, filter, 100);

        if (_dragResult.simplexIndex >= 0 && solver.simplices.count > 0) {
            int particleIndex = solver.simplices[_dragResult.simplexIndex]; // index of the particle in the actor

            var dragPos = _camera.ScreenToWorldPoint(new Vector3(Mouse.current.position.value.x, Mouse.current.position.value.y, -_camera.transform.position.z));
            solver.positions[particleIndex] = math.lerp(solver.positions[particleIndex], new float4(dragPos, 0), 32f * Time.fixedDeltaTime);
        }

        /*
        Calculate an average angular velocity around the center of mass
        
        It will be useful in controlling maximum rotation speed.
        */

        _centerOfMass = CalculateCenterOfMass(_slimeBody);
    }

    private static Vector3 CalculateCenterOfMass(ObiSoftbody body)
    {
        Vector3 com = Vector3.zero;
        if (body.solverIndices.count > 0)
        {
            for (int i = 0; i < body.solverIndices.count; ++i)
            {
                int solverIndex = body.solverIndices[i];
                com += body.GetParticlePosition(solverIndex);
            }
            com /= body.solverIndices.count;
        }
        return com;
    }

    private void Solver_OnSpatialQueryResults(ObiSolver s, ObiNativeQueryResultList queryResults)
    {
        _rayResult = new QueryResult { distanceAlongRay = float.MaxValue, simplexIndex = -1, queryIndex = -1 };
        for (int i = 0; i < queryResults.count; ++i)
        {
            // get the first result along the ray. That is, the one with the smallest distanceAlongRay:
            if (queryResults[i].queryIndex == queryIndex &&
                queryResults[i].distanceAlongRay < _rayResult.distanceAlongRay)
            {
                _rayResult = queryResults[i];
            }
        }
    }

    void Solver_OnCollision(object sender, ObiNativeContactList e)
    {
        var world = ObiColliderWorld.GetInstance();

        // Determine major contact patch direction

        _patches.Clear();

        // just iterate over all contacts in the current frame:
        foreach (Oni.Contact contact in e)
        {
            // if this one is an actual collision:
            if (contact.distance < 0.01)
            {
                /*
                Assumption:

                contact.bodyA is our slime creature
                contact.bodyB is a static collider
                */

                /*
                Filter only for collisions involving our slime creature

                todo:
                - can we simplify/optimize this rejection test by caching some indices?
                */
                int simplexStart = solver.simplexCounts.GetSimplexStartAndSize(contact.bodyA, out _);
                int simplexParticle = solver.simplices[simplexStart];
                var particleActor = solver.particleToActor[simplexParticle];
                if (particleActor == null || particleActor.actor != _slimeBody)
                {
                    continue;
                }

                // ObiColliderBase colA = world.colliderHandles[contact.bodyA].owner; // doesn't work
                ObiColliderBase colB = world.colliderHandles[contact.bodyB].owner;

                // Debug.Log($"collision detected: {contact.bodyA} -> {colB.name}");

                /*
                Cluster together collisions at the same angle as contact patches, for easier input handling

                Todo:
                - reject clustering if positioned too far from cluster candidate
                - may also resort to a more precise way to handle input/control based on full information, later
                */
                if (colB != null) 
                {
                    // do something with the collider.

                    bool wasMerged = false;
                    for (int p = 0; p < _patches.Count; p++)
                    {
                        var patch = _patches[p];
                        if (math.dot(patch.normal, (Vector3)contact.normal) > 0.8f)
                        {
                            patch.position += (Vector3)contact.pointB;
                            patch.weight += 1;

                            _patches[p] = patch;
                            wasMerged = true;
                            break;
                        }
                    }

                    if (!wasMerged) {
                        _patches.Add(new ContactPatch { position = contact.pointB, normal = contact.normal, weight = 1f });
                    }
                }
            }
        }

        for (int p = 0; p < _patches.Count; p++)
        {
            var patch = _patches[p];
            patch.position /= patch.weight;
            _patches[p] = patch;
        }

    }

    private void Update()
    {
        /*
        Drag softbodies by mouse cursor
        */

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (_rayResult.simplexIndex >= 0)
            {
                _dragResult = _rayResult;
            }
        }
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            _dragResult = new QueryResult { distanceAlongRay = float.MaxValue, simplexIndex = -1, queryIndex = -1 };
        }

        /*
        Control slime creature with gamepad, like a platformer
        */

        /*
        Adapt control inputs based on context.

        Rolling is achieved by adding torque, which gets us moving due to friction.
        But it matters *where* that friction is relative to our center of mass, e.g.
        rolling left along the floor is opposite to rolling left along the ceiling.
        */

        Vector3 jumpDirection = Vector3.zero;

        float totalPatchWeights = 0;
        if (_patches.Count > 0)
        {
            var mainPatch = _patches[0];
            for (int p = 0; p < _patches.Count; p++)
            {
                var patch = _patches[p];
                if (patch.weight > mainPatch.weight)
                {
                    mainPatch = patch;
                }

                jumpDirection += patch.normal * patch.weight;
                totalPatchWeights += patch.weight;
            }

            jumpDirection /= totalPatchWeights;
        }
        

        var _pad = Gamepad.current;
        if (_pad != null) {
            _slimeBody.deformationResistance = math.lerp(0.1f, 0.3f, _pad.rightTrigger.value);
            _slimeBody.collisionMaterial.stickiness = math.lerp(_minStickiness, _maxStickiness, _pad.leftTrigger.value);
            _slimeBody.AddTorque(new Vector3(0,0, _pad.leftStick.value.x * -0.2f), ForceMode.VelocityChange);
            _slimeBody.AddForce(new Vector3(_pad.leftStick.value.x, _pad.leftStick.value.y, 0) * 0.2f, ForceMode.VelocityChange);

            if (_pad.buttonSouth.wasPressedThisFrame)
            {
                /*
                Todo: jump needs windup, so it becomes more complex behavior in our game
                but that's great, because anticipation can visibly build as the slime
                *prepares* to jump.
                */
                _slimeBody.AddForce(jumpDirection * 9.81f * 300f, ForceMode.Impulse);
                AddForce(_slimeBody, jumpDirection * 9.81f * 10f, ForceMode.Impulse);
            }
        }
    }

    private Quaternion _faceAnchorRotation = Quaternion.identity;
    private void LateUpdate()
    {
        for (int b = 0; b < _slimeBubbles.Count; b++)
        {
            var oldPos = _slimeBubbles[b].Item1.position - _centerOfMass;
            var pos = (Vector3)_slimeBody.solver.positions[_slimeBody.solverIndices[_slimeBubbles[b].Item2]] - _centerOfMass;
            pos *= 0.9f;
            // pos = math.lerp(oldPos, pos, 12f * Time.deltaTime);
            _slimeBubbles[b].Item1.position = _centerOfMass + pos;
        }

        var _pad = Gamepad.current;

        var com = CalculateCenterOfMass(_slimeBody);
        var faceAnchorRotation = Quaternion.Euler(_pad.leftStick.value.y * 33f, _pad.leftStick.value.x * -45f, 0f); // _camera.transform.rotation
        _faceAnchorRotation = Quaternion.Slerp(_faceAnchorRotation, faceAnchorRotation, Time.time * 3f);
        _faceAnchor = Matrix4x4.TRS(com, _faceAnchorRotation, Vector3.one);
        _slimeMaterial.SetMatrix("_FaceAnchor", _faceAnchor.inverse);
        _slimeMaterial.SetVector("_CenterOfMass", (Vector4)com);

        if (Time.timeAsDouble >= _lastBlinkTime + _blinkDuration)
        {
            _blinkDuration = _rng.NextFloat(1f, 4);
            StartCoroutine(BlinkAsync());
            _lastBlinkTime = Time.timeAsDouble;
        }
    }

    private static void AddForce(ObiSoftbody body, Vector3 force, ForceMode mode)
    {
        /*
        A method of applying force to the body that only applies full force to the
        inner core, falling off towards the outer edges and the skin.

        The goal is to introduce more of a visible wobble

        Todo:
        - calculate which particles to affect, and how much, from the rest state. Do it once, at startup.
        - respect inverse-mass
        - respect forcemode
        - integrate falloff over shape to normalize applied force, such that total force is the same as uniform application
        */

        float timeStep = mode == ForceMode.VelocityChange ? Time.fixedDeltaTime : 1f / Time.fixedDeltaTime;

        var com = CalculateCenterOfMass(body);
        for (int p = 0; p < body.solverIndices.count; ++p)
        {
            int solverIndex = body.solverIndices[p];
            var radius = math.length((Vector3)body.solver.positions[solverIndex] - com);
            body.solver.externalForces[solverIndex] += (Vector4)(force * (math.remap(0.5f, 1f, 1f, 0f, math.saturate(radius)) * timeStep));
        }
    }

    public override void DrawShapes(Camera cam)
    {
        // Todo: draw emote decorations around the character
        using (Draw.Command(cam)) // UnityEngine.Rendering.Universal.RenderPassEvent
        {
            Draw.ThicknessSpace = ThicknessSpace.Pixels;
            Draw.RadiusSpace = ThicknessSpace.Meters;
            Draw.Thickness = 1f;
            Draw.BlendMode = ShapesBlendMode.Opaque;

            bool rayHitSomething = _rayResult.simplexIndex >= 0;
            bool draggingSomething = _dragResult.simplexIndex >= 0;

            Draw.Color = draggingSomething ? Color.orange : (rayHitSomething ? Color.greenYellow : Color.grey);
            Draw.Line(_ray.origin, _ray.origin + _ray.direction * 100f);

            if (rayHitSomething)
            {
                Draw.Sphere(_rayResult.queryPoint, 0.2f);
            }

            if (draggingSomething && solver.simplices.count > 0)
            {
                int particleIndex = solver.simplices[_dragResult.simplexIndex]; // index of the particle in the actor

                var dragPos = _camera.ScreenToWorldPoint(new Vector3(Mouse.current.position.value.x, Mouse.current.position.value.y, -_camera.transform.position.z));
                var particlePos = solver.positions[particleIndex];

                Draw.Line(dragPos, particlePos);
                Draw.Sphere(particlePos, 0.2f);
            }
        }

        using (Draw.Command(cam, UnityEngine.Rendering.Universal.RenderPassEvent.AfterRenderingPostProcessing))
        {
            Draw.Color = Color.blanchedAlmond;

            // Draw.Sphere(_centerOfMass, 0.3f);

            /*
            Draw contacts
            */
            // for (int p = 0; p < _patches.Count; p++)
            // {
            //     var patch = _patches[p];
            //     Draw.Line(patch.position, patch.position + patch.normal);
            // }

            // if (_patches.Count > 0)
            // {
            //     var mainPatch = _patches[0];
            //     for (int p = 0; p < _patches.Count; p++)
            //     {
            //         var patch = _patches[p];
            //         if (patch.weight > mainPatch.weight)
            //         {
            //             mainPatch = patch;
            //         }
            //     }

            //     Vector3 direction = math.cross(mainPatch.normal, new float3(0,0,1));
            //     Draw.Color = Color.lightSalmon;
            //     Draw.Line(_centerOfMass, _centerOfMass + direction);
            // }

            Draw.Color = Color.red;
            Draw.Line(_faceAnchor * Vector3.zero, _faceAnchor * Vector3.right);
            Draw.Color = Color.green;
            Draw.Line(_faceAnchor * Vector3.zero, _faceAnchor * Vector3.up);
            Draw.Color = Color.blue;
            Draw.Line(_faceAnchor * Vector3.zero, _faceAnchor * Vector3.forward);
        }
    }

    private IEnumerator BlinkAsync()
    {
        float eyeOpenScale = 1f;

        float time = 0;
        float blinkDur = _rng.NextFloat(0.2f, 0.3f);
        while (time < blinkDur)
        {
            float blinkLerp = math.saturate(time / blinkDur);
            float scale = eyeOpenScale * (0.5f + 0.5f * math.cos(blinkLerp * math.PI2));
            _slimeMaterial.SetFloat("_Blink", scale);

            time += Time.deltaTime;
            yield return new WaitForEndOfFrame();
        }

        _slimeMaterial.SetFloat("_Blink", eyeOpenScale);
    }
}
