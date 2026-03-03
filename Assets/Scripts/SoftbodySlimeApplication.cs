using UnityEngine;
using Obi;
using UnityEngine.InputSystem;
using Shapes;
using Unity.Mathematics;

public class SoftbodySlimeApplication : ImmediateModeShapeDrawer
{
    [SerializeField] private Camera _camera;

    public ObiSolver solver;
    int filter;
    int queryIndex;
    
    Ray _ray;
    QueryResult _rayResult = new QueryResult { distanceAlongRay = float.MaxValue, simplexIndex = -1, queryIndex = -1 };
    QueryResult _dragResult = new QueryResult { distanceAlongRay = float.MaxValue, simplexIndex = -1, queryIndex = -1 };

    private void Start()
    {
        filter = ObiUtils.MakeFilter(ObiUtils.CollideWithEverything, 0);
        solver.OnSpatialQueryResults += Solver_OnSpatialQueryResults;
        solver.OnSimulationStart += Solver_OnSimulate;

       
    }

    private void OnDestroy()
    {
        solver.OnSpatialQueryResults -= Solver_OnSpatialQueryResults;
        solver.OnSimulationStart -= Solver_OnSimulate;
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
