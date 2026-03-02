using System.Collections;
using Shapes;
using Unity.Mathematics;
using UnityEngine;
using Rng = Unity.Mathematics.Random;

/*
Todo:
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

    [SerializeField] private Transform _body;
    [SerializeField] private Transform _head;
    [SerializeField] private Animator _animator;
    [SerializeField] private SlimeFaceRenderer _face;

    private Transform _transform;

    private SlimeFaceRenderer.MouthShape _mouthShape;
    private SlimeFaceRenderer.MouthShape _eyebrowShape;

    private Quaternion _baseHeadRotation;
    private Vector3 _mouseCursorWorld = new Vector3(0, 0, -1);

    private double _lastBlinkTime = -1;
    private float _blinkDuration = 3;

    private Rng _rng;

    public Transform Transform
    {
        get => _transform;
    }

  
    private void Awake()
    {
        _rng = new Rng(1234);
        _transform = gameObject.GetComponent<Transform>();

        _baseHeadRotation = Quaternion.Inverse(_transform.rotation) * _head.rotation;
       
        _mouthShape = SlimeFaceRenderer.MouthShape.RoundOpen;

        _face.Blink = 1;
    }

    void Update()
    {
        if (Time.timeAsDouble >= _lastBlinkTime + _blinkDuration)
        {
            _blinkDuration = _rng.NextFloat(1f, 4);
            StartCoroutine(BlinkAsync());
            _lastBlinkTime = Time.timeAsDouble;
        }

        _animator.SetFloat("WalkSpeed", 0f);
    }

    private void LateUpdate()
    {
        _head.rotation = Quaternion.LookRotation(_mouseCursorWorld) * _baseHeadRotation;
    }

    public void SetMouseCursorWorld(Vector3 cursorWorld)
    {
        _mouseCursorWorld = cursorWorld;
    }

    public override void DrawShapes(Camera cam)
    {
    
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