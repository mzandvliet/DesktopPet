using System.Collections;
using Shapes;
using Unity.Mathematics;
using UnityEngine;
using Rng = Unity.Mathematics.Random;

/*
Todo:
- go isometric camera?
- establish direct relation between world Z and desktop Z depth
- use a camera further away with narrow fov to get less perspective distortion at screen edges

Wants, Needs & Desires
- eat (virtual stuff, real files)
- listen
- dance
- watch
- play
    - throw a ball, like it's a dog
    - watch you play specific games, like Space Warlord
*/

public class Slime : ImmediateModeShapeDrawer
{
    [SerializeField] private Camera _camera; // needs this to know where is valid to move

    [SerializeField] private LayerMask _characterMask;
    [SerializeField] private LayerMask _foodMask;

    [SerializeField] private Transform _body;
    [SerializeField] private Transform _head;
    [SerializeField] private Animator _animator;
    [SerializeField] private SlimeFaceRenderer _face;

    private Transform _transform;

    private CharacterState _state;
    private CharacterIdleState _idleState;
    private Collider[] _collidersNearby;

    private float _idleDurationTime;
    private float _idleTimer;

    private double _lastBlinkTime = -1;
    private float _blinkDuration = 3;

    private SlimeFaceRenderer.MouthShape _mouthShape;
    private SlimeFaceRenderer.MouthShape _eyebrowShape;

    private Vector3 _mouseCursorWorld = new Vector3(0, 0, -1);

    private Transform _target;
    private Vector3 _moveTargetLocation = new Vector3(0,0,0);
    private Vector3 _moveTargetDirection = new Vector3(0, 0, 1);
    private Vector3 _lookDirection = new Vector3(0, 0, -1);


    private Rng _rng;

    public Transform Transform
    {
        get => _transform;
    }

    public CharacterState State
    {
        get => _state;
    }

    public CharacterIdleState IdleState
    {
        get => _idleState;
    }


    private void Awake()
    {
        _rng = new Rng(1234);
        _transform = gameObject.GetComponent<Transform>();
       
        _mouthShape = SlimeFaceRenderer.MouthShape.RoundOpen;

        _face.Blink = 1;

        _collidersNearby = new Collider[64];

        ChangeState(CharacterState.Idle);
    }

    void Update()
    {
        if (Time.timeAsDouble >= _lastBlinkTime + _blinkDuration)
        {
            _blinkDuration = _rng.NextFloat(1f, 4);
            StartCoroutine(BlinkAsync());
            _lastBlinkTime = Time.timeAsDouble;
        }

        switch (_state)
        {
            case CharacterState.Idle:
                UpdateIdleState();
                break;
            case CharacterState.Walking:
                UpdateWalkingState();
                break;
            default:
                Debug.LogError($"Unkonwn character state: {_state}");
                break;
        }
    }

    private void ChangeState(CharacterState state)
    {
        ChangeState(state, null);
    }

    private void ChangeState(CharacterState state, CharacterIdleState? idleState)
    {
        Debug.Log($"Change state: {_state} -> {state}");
        _state = state;
        switch (state)
        {
            case CharacterState.Idle:
                if (idleState.HasValue)
                {
                    _idleState = idleState.Value;
                }
                else
                {
                    _idleState = (CharacterIdleState)_rng.NextInt(0, CharacterIdleStateMax);
                }
                EnterIdleState();
                break;
            case CharacterState.Walking:
                EnterWalkingState();
                break;
            default:
                Debug.LogError($"Unknown character state: {state}");
                break;
        }
    }

    void EnterIdleState()
    {
        // Eat if we're within range of food
        int numColliders = Physics.OverlapSphereNonAlloc(_transform.position, 1, _collidersNearby, _foodMask.value);
        for (int c = 0; c < numColliders; c++)
        {
            var food = _collidersNearby[c].gameObject.GetComponent<Food>();
            if (food != null)
            {
                GameObject.Destroy(food.gameObject);
            }
        }

        _idleDurationTime = _rng.NextFloat(3f, 8f);
        _idleTimer = 0f;
    }

    void UpdateIdleState()
    {
        var lookTarget = _idleState == CharacterIdleState.LookAtCursor ? _mouseCursorWorld : _camera.transform.position;
        _lookDirection = math.normalize(lookTarget - _head.position);
        
        var bodyDirection = _lookDirection;
        bodyDirection.y *= 0.1f;
        bodyDirection = math.normalize(bodyDirection);
        var bodyRotation = Quaternion.LookRotation(bodyDirection);
        _transform.rotation = Quaternion.Slerp(_transform.rotation, bodyRotation, 1f * Time.deltaTime);

        _animator.SetFloat("WalkSpeed", 0f);

        _idleTimer += Time.deltaTime;
        if (_idleTimer > _idleDurationTime)
        {
            ChangeState(CharacterState.Walking);
        }
    }

    void EnterWalkingState()
    {
        /*
        Todo:
        - decide on Z depth target
        - use a pathfinding technique to walk between open windows
        */

        int numColliders = Physics.OverlapSphereNonAlloc(_transform.position, 100, _collidersNearby, _foodMask.value);
        float closestFoodDist = float.PositiveInfinity;
        Food closestFood = null;
        for (int c = 0; c < numColliders; c++)
        {
            var food = _collidersNearby[c].gameObject.GetComponent<Food>();
            if (food == null)
            {
                continue;
            }

            var foodDist = math.lengthsq(_transform.position - food.transform.position);
            if (foodDist < closestFoodDist)
            {
                closestFood = food;
                closestFoodDist = foodDist;
            }
        }

        if (closestFood != null)
        {
            _target = closestFood.transform;
            _moveTargetLocation = closestFood.transform.position;
        }
        else
        {
            _target = null;

            var bounds = _camera.ScreenToWorldPoint(new Vector3(0, 0, -_camera.transform.position.z));
            bounds = math.abs(bounds);

            _moveTargetLocation = new Vector3(
                _rng.NextFloat(-0.5f * bounds.x, 0.5f * bounds.x),
                _rng.NextFloat(-0.5f * bounds.y, 0.5f * bounds.y),
                _rng.NextFloat(-4f, 4f));
        }

        var targetDir = _moveTargetLocation - _transform.position;
        _moveTargetDirection = math.normalize(targetDir);
    }

    void UpdateWalkingState()
    {
        if (_target != null)
        {
            // update target information if we have a target reference
            _moveTargetLocation = _target.position;
            _moveTargetDirection = math.normalize(_target.position - _transform.position);
        }
        var targetDelta = _moveTargetLocation - _transform.position;
        var targetDist = math.length(targetDelta);

        if (targetDist < 0.1f)
        {
            ChangeState(CharacterState.Idle);
            return;
        }

        const float charMoveSpeed = 4f;

        var bodyDirection = _moveTargetDirection;
        bodyDirection.y *= 0.1f;
        bodyDirection = math.normalize(bodyDirection);
        var bodyRotation = Quaternion.LookRotation(bodyDirection);
        _transform.rotation = Quaternion.Slerp(_transform.rotation, bodyRotation, 6f * Time.deltaTime);

        // if (math.dot(_transform.forward, _targetDirection.Value) < 0.5f)
        // {
        //     // Wait until we're looking roughly in the target direction before actually walking there
        //     return;
        // }

        // move
        _transform.position += (Vector3)math.normalize(targetDelta) * (charMoveSpeed * Time.deltaTime);

        // Todo: derive a useful notion of world-space units to pixel units to determine useful speeds? Perspective muddles this though...

        _animator.SetFloat("WalkSpeed", 1f);
    }

    private Quaternion _headRotationWorld = Quaternion.identity;

    private void LateUpdate()
    {
        /*
        Head look behavior

        - Applied in LateUpdate to override Animator results
        - head rotation calculated in separate state, in world space, to avoid hierarchical transform issues
        - head rotation limited to 45 degree offset from its body
        */

        const float maxHeadAngle = 45f;

        const float noiseSpeed = 0.5f;
        var focusWobble = new Vector3(
            -1f + 2f * Mathf.PerlinNoise1D(math.fmod(Time.time * noiseSpeed + 0.571f, 1f)),
            -1f + 2f * Mathf.PerlinNoise1D(math.fmod(Time.time * noiseSpeed + 5.113f, 1f)),
            -1f + 2f * Mathf.PerlinNoise1D(math.fmod(Time.time * noiseSpeed + 6.733f, 1f))
        ) * 0.25f;

        var lookDir = math.normalize(_lookDirection + focusWobble);

        var targetWorldLookRotation = Quaternion.LookRotation(lookDir);
        var newHeadWorldRotation = Quaternion.RotateTowards(_transform.rotation, targetWorldLookRotation, maxHeadAngle);

        // var horizontalTilt = Vector3.SignedAngle(_transform.forward, newHeadWorldRotation * Vector3.forward, _transform.up);
        // horizontalTilt = math.clamp(horizontalTilt, -maxHeadAngle, maxHeadAngle) / maxHeadAngle;
        // var headLocalTilt = Quaternion.AngleAxis(horizontalTilt * 30f, Vector3.forward);
        // Debug.Log(horizontalTilt * 30f);

        _headRotationWorld = Quaternion.Slerp(_headRotationWorld, newHeadWorldRotation, 6f * Time.deltaTime);

        // Apply only when requested?
        _head.rotation = _headRotationWorld;
    }

    public void SetMouseCursorWorld(Vector3 cursorWorld)
    {
        _mouseCursorWorld = cursorWorld;
    }

    public override void DrawShapes(Camera cam)
    {
        if (!DesktopHook.Instance.ShowDebug)
        {
            return;
        }

        // Todo: draw emote decorations around the character
        using (Draw.Command(cam)) // UnityEngine.Rendering.Universal.RenderPassEvent
        {
            Draw.ThicknessSpace = ThicknessSpace.Pixels;
            Draw.RadiusSpace = ThicknessSpace.Meters;
            Draw.Thickness = 1f;
            Draw.BlendMode = ShapesBlendMode.Opaque;

            Draw.Line(_transform.position, _moveTargetLocation, Color.black);
            Draw.Sphere(_moveTargetLocation, 0.2f, Color.black);

            Draw.Line(_head.position, _mouseCursorWorld, Color.greenYellow);
            Draw.Sphere(_mouseCursorWorld, 0.2f, Color.greenYellow);
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
            _face.Blink = scale;

            time += Time.deltaTime;
            yield return new WaitForEndOfFrame();
        }

        _face.Blink = eyeOpenScale;
    }



    public enum CharacterState
    {
        Idle,
        Walking
    }

    public const int CharacterIdleStateMax = 2;
    public enum CharacterIdleState
    {
        LookAtCursor,
        LookAtPlayer
    }
}

public abstract class Need
{
    public float value; // 0-1
    public float urgency => CalculateUrgency();
    
    public abstract Action[] GetSatisfyingActions();
    protected abstract float CalculateUrgency();
}

public abstract class Action
{
    public abstract bool CanExecute();
    public abstract IEnumerator Execute();
    public abstract float GetUtility(Need need);
}