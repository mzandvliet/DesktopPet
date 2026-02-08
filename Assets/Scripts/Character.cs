using System.Collections;
using System.Linq;
using Shapes;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using Rng = Unity.Mathematics.Random;

/*
Todo:
- bring in the updated HTN library so we can flex complex states
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

public class Character : ImmediateModeShapeDrawer
{
    [SerializeField] private Camera _camera; // needs this to know where is valid to move
    [SerializeField] private LayerMask _theaterLayer;

    [SerializeField] private Transform _body;

    [SerializeField] private Transform _handLeft;
    [SerializeField] private Transform _handRight;

    [SerializeField] private Transform _eyeLeft;
    [SerializeField] private Transform _eyeRight;

    private Transform _transform;
    private Rng _rng;

    private CharacterState _state;
    private CharacterIdleState _idleState;
    private CharacterMouthShape _mouthShape;
    private CharacterEyebrowShape _eyebrowShape;

    private float _idleDurationTime;
    private float _idleTimer;

    private Vector3 _mouseCursorWorld = new Vector3(0,0,-1);
    private float _jumpTimer = -1;

    private double _lastBlinkTime = -1;
    private float _blinkDuration = 3;

    private Vector3 _moveTargetLocation;

    private Vector3 _bodyBasePos;
    private Vector3 _handLeftBasePos;
    private Vector3 _handRightBasePos;

    private Collider[] _collidersNearby;

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
        _transform = gameObject.GetComponent<Transform>();
        _rng = RngManager.CreateRng();

        _collidersNearby = new Collider[64];

        _bodyBasePos = _body.localPosition;
        _handLeftBasePos = _handLeft.localPosition;
        _handRightBasePos = _handRight.localPosition;

        ChangeState(CharacterState.Idle);
    }

    public void SetMouseCursorWorld(Vector3 cursorWorld)
    {
        _mouseCursorWorld = cursorWorld;
    }

    public void OnClicked()
    {
        _jumpTimer = 0;

        if (_state == CharacterState.Walking)
        {
            ChangeState(CharacterState.Idle);
        }

        _mouthShape = CharacterMouthShape.RoundOpen;
    }

    // Update is called once per frame
    void Update()
    {
        if (Time.timeAsDouble >= _lastBlinkTime + _blinkDuration)
        {
            _blinkDuration = _rng.NextFloat(1f, 4);
            StartCoroutine(BlinkAsync());
            _lastBlinkTime = Time.timeAsDouble;
        }

        /*
        Attention mechanisms:
        if mouse moves a lot, especially near it, that draws attention
        */

        int numColliders = Physics.OverlapSphereNonAlloc(_transform.position, 1, _collidersNearby, _theaterLayer.value);
        for (int c = 0; c < numColliders; c++)
        {
            var character = _collidersNearby[c].transform.parent?.gameObject.GetComponent<Character>();
            if (character != null && character != this)
            {
                var delta = _transform.position - character.transform.position;
                var dist = math.pow(math.clamp(math.length(delta) / 2f, 0.01f, 1f), 0.5f);
                _transform.position += delta.normalized * (1f - dist) * 2f * Time.deltaTime;
            }
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

        UpdateJumpAnim();
    }

    private void ChangeState(CharacterState state)
    {
        ChangeState(state, null);
    }

    private void ChangeState(CharacterState state, CharacterIdleState? idleState)
    {
        _state = state;
        switch (state)
        {
            case CharacterState.Idle:
                if (idleState.HasValue)
                {
                    _idleState = idleState.Value;
                } else
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

    void UpdateJumpAnim()
    {
        if (_jumpTimer >= 0)
        {
            const float jumpDur = 0.66f;
            float jumpLerp = math.saturate(_jumpTimer / jumpDur);
            float y = math.sin(jumpLerp * math.PI) * 2f;
            _body.localPosition = new Vector3(0, y, 0);

            _jumpTimer += Time.deltaTime;

            if (_jumpTimer >= jumpDur)
            {
                _jumpTimer = -1;
            }
        }
    }

    void EnterIdleState()
    {
        // Eat if we're within range of food
        int numColliders = Physics.OverlapSphereNonAlloc(_transform.position, 1, _collidersNearby, _theaterLayer.value);
        for (int c = 0; c < numColliders; c++)
        {
            var food = _collidersNearby[c].gameObject.GetComponent<Food>();
            if (food != null)
            {
                GameObject.Destroy(food.gameObject);
            }
        }

        _mouthShape = (CharacterMouthShape)_rng.NextInt(0, CharacterMouthShapeMax);
        _eyebrowShape = (CharacterEyebrowShape)_rng.NextInt(0, CharacterEyebrowShapeMax);
        // _eyebrowShape = CharacterEyebrowShape.Concerned;

        _idleDurationTime = _rng.NextFloat(2f, 4f);
        _idleTimer = 0f;
    }

    void UpdateIdleState()
    {
        var lookTarget = _idleState == CharacterIdleState.LookAtCursor ? _mouseCursorWorld : _camera.transform.position;

        var lookDir = lookTarget - _transform.position;
        var lookRot = Quaternion.LookRotation(lookDir);
        _transform.rotation = Quaternion.Slerp(_transform.rotation, lookRot, 4f * Time.deltaTime);

        // _transform.localScale = Vector3.one * (3f + (float)math.sin(Time.timeAsDouble * math.PI2 * 0.5f));

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

        int numColliders = Physics.OverlapSphereNonAlloc(_transform.position, 100, _collidersNearby, _theaterLayer.value);
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
            _moveTargetLocation = closestFood.transform.position;
            return;
        }

        var bounds = _camera.ScreenToWorldPoint(new Vector3(0, 0, -_camera.transform.position.z));
        bounds = math.abs(bounds);

        _moveTargetLocation = new Vector3(
            _rng.NextFloat(-0.5f * bounds.x, 0.5f * bounds.x),
            _rng.NextFloat(-0.5f * bounds.y, 0.5f * bounds.y),
            _rng.NextFloat(-4f, 4f));
    }

    void UpdateWalkingState()
    {
        var targetDelta = _moveTargetLocation - _transform.position;
        var targetDist = math.length(targetDelta);

        if (targetDist < 0.05f)
        {
            ChangeState(CharacterState.Idle);
            return;
        }

        const float charMoveSpeed = 2f;

        var targetDir = targetDelta / targetDist;
        var lookDir = _moveTargetLocation - _transform.position;

        var lookRot = Quaternion.LookRotation(lookDir);
        _transform.rotation = Quaternion.Slerp(_transform.rotation, lookRot, 4f * Time.deltaTime);

        if (math.dot(_transform.forward, targetDir.normalized) < 0.5f)
        {
            // Wait until we're looking roughly in the target direction before actually walking there
            return;
        }
        
        // move
        _transform.position += targetDir * (charMoveSpeed * Time.deltaTime);

        // Todo: derive a useful notion of world-space units to pixel units to determine useful speeds? Perspective muddles this though...
        
        if (lookDir.z < 0) {
            // if moving towards screen, tilt look direction such that face is visible to player
            lookDir.z = transform.position.z - 2f; 
        }

        /* Animate */

        const float charBopSpeed = 3f;

        _body.localPosition = _bodyBasePos + new Vector3(0f, 0.05f * math.sin(Time.time * math.PI2 * charBopSpeed), 0f);
        _handLeft.localPosition = _handLeftBasePos + new Vector3(
            0f,
            -0.1f * math.abs(math.cos(Time.time * math.PI2 * charBopSpeed * 0.5f)),
            +0.1f * math.sin(Time.time * math.PI2 * charBopSpeed * 0.5f));
        _handRight.localPosition = _handRightBasePos + new Vector3(
            0f,
            -0.1f * math.abs(math.cos(Time.time * math.PI2 * charBopSpeed * 0.5f)),
            -0.1f * math.sin(Time.time * math.PI2 * charBopSpeed * 0.5f));
    }

    private const float ZOffset = 0.01f;

    public override void DrawShapes(Camera cam)
    {
        using (Draw.Command(cam)) // UnityEngine.Rendering.Universal.RenderPassEvent.BeforeRendering
        {
            Draw.ThicknessSpace = ThicknessSpace.Meters;
            Draw.RadiusSpace = ThicknessSpace.Meters;
            Draw.Thickness = 0.02f;
            Draw.BlendMode = ShapesBlendMode.Opaque;

            Draw.Color = Color.black;

            var mouthIntrinsicPos = new Vector2(0f, -0.25f);

            var mouthLocalPos =
                Vector3.forward * (_body.localScale.z * (0.5f + ZOffset)) +
                Vector3.up * (_body.localScale.y * mouthIntrinsicPos.y);
            var mouthWorldPos = _body.TransformPoint(mouthLocalPos);
            
            switch (_mouthShape)
            {
                case CharacterMouthShape.None:
                    break;
                case CharacterMouthShape.RoundOpen:
                    Draw.Disc(mouthWorldPos, _body.rotation, 0.1f);
                    break;
                case CharacterMouthShape.TriangleOpen:
                    Draw.Triangle(
                        mouthWorldPos + _body.TransformDirection(new Vector3(0f, +0.1f, 0f)),
                        mouthWorldPos + _body.TransformDirection(new Vector3(+0.1f, 0f, 0f)),
                        mouthWorldPos + _body.TransformDirection(new Vector3(-0.1f, 0f, 0f))
                    );
                    break;
                case CharacterMouthShape.BigSmile:
                    var path = new PolygonPath();
                    var localCenter = new Vector2(0, -0.33f);
                    path.AddPoint(localCenter + new Vector2(- 0.2f, + 0.12f));
                    path.AddPoint(localCenter + new Vector2(+ 0.2f, + 0.12f));
                    path.AddPoint(localCenter + new Vector2(+ 0.1f, - 0.1f));
                    path.AddPoint(localCenter + new Vector2(- 0.1f, - 0.1f));
                    Draw.PushMatrix();
                    Draw.Matrix = Matrix4x4.TRS(
                        _body.TransformPoint(Vector3.forward * (_body.localScale.z * (0.5f + ZOffset))),
                        _body.rotation,
                        _body.localScale
                    );
                    Draw.Polygon(
                       path
                    );
                    Draw.PopMatrix();
                    break;
                case CharacterMouthShape.LineFlat:
                    Draw.Line(
                        mouthWorldPos + _body.TransformDirection(new Vector3(+0.1f, 0f, 0f)),
                        mouthWorldPos + _body.TransformDirection(new Vector3(-0.1f, 0f, 0f))
                    );
                    break;
            }

            DrawEyebrow(_body, _eyeLeft, _eyebrowShape);
            DrawEyebrow(_body, _eyeRight, _eyebrowShape);
        }
    }

    private static void DrawEyebrow(Transform body, Transform eye, CharacterEyebrowShape shape)
    {
        // Draw.Sphere(eyeTransform.position, 0.25f);

        int side = (int)math.sign(eye.localPosition.x);

        Draw.Color = Color.black;

        var path = new PolylinePath();

        switch (shape)
        {
            case CharacterEyebrowShape.None:
                break;
            case CharacterEyebrowShape.Neutral:
                {
                    var a = new Vector2(-0.15f, 0.1f);
                    var d = new Vector2(0.15f, 0.11f);
                    var b = a + new Vector2(0.1f, 0.01f);
                    var c = d + new Vector2(-0.1f, 0.00f);
                    path.AddPoint(a);
                    path.BezierTo(b, c, d, 8);
                }
                break;
            case CharacterEyebrowShape.Raised:
                {
                    var a = new Vector2(-0.15f, 0.1f);
                    var d = new Vector2(0.1f, 0.2f);
                    var b = a + new Vector2(0.1f, 0.05f);
                    var c = d + new Vector2(-0.1f, 0.05f);
                    path.AddPoint(a);
                    path.BezierTo(b, c, d, 8);
                }
                break;
            case CharacterEyebrowShape.Concerned:
            {
                {
                    var a = new Vector2(-0.1f, 0.1f);
                    var d = new Vector2(0.1f, 0.15f);
                    var b = a + new Vector2(0.1f, 0f);
                    var c = d + new Vector2(-0.1f, -0.1f);
                    
                    path.AddPoint(a);
                    path.BezierTo(b, c, d, 8);
                }
                break;
            }
            case CharacterEyebrowShape.Angry:
            {
                {
                    var a = new Vector2(-0.05f, 0.2f);
                    var d = new Vector2(0.15f, 0.1f);
                    var b = a + new Vector2(0.1f, 0f);
                    var c = d + new Vector2(-0.1f, 0.05f);

                    path.AddPoint(a);
                    path.BezierTo(b, c, d, 8);
                }
                break;
            }
        }

        var localCenter = new Vector3(0, 0.3f);
        Draw.PushMatrix();
        var localScale = body.localScale;
        localScale.x *= -side;
        Draw.Matrix = Matrix4x4.TRS(
            eye.TransformPoint(Vector3.forward * (body.localScale.z * ZOffset) + localCenter),
            body.rotation,
            localScale
        );
        Draw.Polyline(
            path
        );
        Draw.PopMatrix();
    }

    private IEnumerator BlinkAsync()
    {
        float eyeOpenScale = 0.2f;

        float time = 0;
        float blinkDur = _rng.NextFloat(0.15f, 0.3f);
        Vector3 eyeScale;
        while (time < blinkDur)
        {
            float blinkLerp = math.saturate(time / blinkDur);
            float scale = eyeOpenScale * math.cos(blinkLerp * math.PI2);
            eyeScale = Vector3.one * scale;
            _eyeLeft.localScale = eyeScale;
            _eyeRight.localScale = eyeScale;

            time += Time.deltaTime;
            yield return new WaitForEndOfFrame();
        }

        eyeScale = Vector3.one * eyeOpenScale;
        _eyeLeft.localScale = eyeScale;
        _eyeRight.localScale = eyeScale;
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

    public const int CharacterMouthShapeMax = 4;
    public enum CharacterMouthShape
    {
        None,
        RoundOpen,
        TriangleOpen,
        BigSmile,
        LineFlat
    }

    public const int CharacterEyebrowShapeMax = 5;
    public enum CharacterEyebrowShape
    {
        None,
        Neutral,
        Raised,
        Concerned,
        Angry,
    }
}
