using System.Collections;
using Shapes;
using Unity.Mathematics;
using UnityEngine;
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

public class Slime : ImmediateModeShapeDrawer
{
    [SerializeField] private Camera _camera; // needs this to know where is valid to move

    [SerializeField] private Transform _body;
    [SerializeField] private Transform _head;
    [SerializeField] private Animator _animator;

    private Transform _transform;

    private SlimeFaceRenderer.MouthShape _mouthShape;
    private SlimeFaceRenderer.MouthShape _eyebrowShape;

    private Vector3 _bodyBasePos;
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
       
        _mouthShape = SlimeFaceRenderer.MouthShape.RoundOpen;
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
        _head.LookAt(_mouseCursorWorld);
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
        float eyeOpenScale = 0.45f;

        float time = 0;
        float blinkDur = _rng.NextFloat(0.15f, 0.3f);
        Vector3 eyeScale;
        while (time < blinkDur)
        {
            float blinkLerp = math.saturate(time / blinkDur);
            float scale = eyeOpenScale * math.cos(blinkLerp * math.PI2);
            eyeScale = Vector3.one * scale;

            time += Time.deltaTime;
            yield return new WaitForEndOfFrame();
        }

        eyeScale = Vector3.one * eyeOpenScale;
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
