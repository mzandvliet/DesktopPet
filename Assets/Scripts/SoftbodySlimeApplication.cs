using UnityEngine;
using Obi;
using UnityEngine.InputSystem;
using Shapes;
using Unity.Mathematics;

public class SoftbodySlimeApplication : ImmediateModeShapeDrawer
{
    [SerializeField] private Camera _camera;
    [SerializeField] private ObiSoftbody _slimeBody;

    public ObiSolver solver;
    int filter;
    int queryIndex;
    
    Ray _ray;
    QueryResult _rayResult = new QueryResult { distanceAlongRay = float.MaxValue, simplexIndex = -1, queryIndex = -1 };
    QueryResult _dragResult = new QueryResult { distanceAlongRay = float.MaxValue, simplexIndex = -1, queryIndex = -1 };

    private void Start()
    {
        filter = ObiUtils.MakeFilter(ObiUtils.CollideWithEverything, 0);
        
    }

    public override void OnEnable()
    {
        solver.OnSpatialQueryResults += Solver_OnSpatialQueryResults;
        solver.OnSimulationStart += Solver_OnSimulate;
        solver.OnCollision += Solver_OnCollision;
    }

    public override void OnDisable()
    {
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

        // for (int i = 0; i < _slimeBody.solverIndices.count; ++i)
        // {

        //     int solverIndex = _slimeBody.solverIndices[i];

        //     // if the particle is visually close enough to the anchor, fix it.
        //     // _slimeBody.GetParticlePosition(solverIndex)
        //     // _slimeBody.solver.velocities[solverIndex] = Vector3.zero;
        //     // _slimeBody.solver.invMasses[solverIndex] = 0;
        //     // _slimeBody.solver.resi = Vector3.zero;
        // }
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

        // just iterate over all contacts in the current frame:
        foreach (Oni.Contact contact in e)
        {
            // if this one is an actual collision:
            if (contact.distance < 0.01)
            {
                ObiColliderBase col = world.colliderHandles[contact.bodyB].owner;
                if (col != null)
                {
                    // do something with the collider.
                }
            }
        }
    }

    private void Update()
    {
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

        var _pad = Gamepad.current;
        _slimeBody.deformationResistance = math.lerp(0.5f, 0.05f, _pad.rightTrigger.value);
        _slimeBody.collisionMaterial.stickiness = math.lerp(0.05f, 0.5f, _pad.leftTrigger.value);
        _slimeBody.AddTorque(new Vector3(0,0, _pad.leftStick.value.x * -0.1f), ForceMode.VelocityChange);
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
    }
}
