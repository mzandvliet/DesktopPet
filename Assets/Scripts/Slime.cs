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
    [SerializeField] private Transform _torso;

    [SerializeField] private Transform _eyeLeft;
    [SerializeField] private Transform _eyeRight;

    private Transform _transform;

    private CharacterMouthShape _mouthShape;
    private CharacterEyebrowShape _eyebrowShape;

    private Vector3 _bodyBasePos;

    private double _lastBlinkTime = -1;
    private float _blinkDuration = 3;

    private PolygonPath _mouthPath;
    private PolylinePath[] _eyePaths;

    private Rng _rng;

    public Transform Transform
    {
        get => _transform;
    }

  
    private void Awake()
    {
        _rng = new Rng(1234);

        _transform = gameObject.GetComponent<Transform>();
       
        _mouthPath = new PolygonPath();
        _eyePaths = new PolylinePath[2];
        _eyePaths[0] = new PolylinePath();
        _eyePaths[1] = new PolylinePath();

        _mouthShape = CharacterMouthShape.RoundOpen;
    }

    void Update()
    {
        if (Time.timeAsDouble >= _lastBlinkTime + _blinkDuration)
        {
            _blinkDuration = _rng.NextFloat(1f, 4);
            StartCoroutine(BlinkAsync());
            _lastBlinkTime = Time.timeAsDouble;
        }
    }

    private const float ZOffset = 0.01f;

    public override void DrawShapes(Camera cam)
    {
        using (Draw.Command(cam, UnityEngine.Rendering.Universal.RenderPassEvent.AfterRenderingOpaques))
        {
            Draw.ThicknessSpace = ThicknessSpace.Meters;
            Draw.RadiusSpace = ThicknessSpace.Meters;
            Draw.Thickness = 0.02f;
            Draw.BlendMode = ShapesBlendMode.Opaque;

            DrawMouth(_body, _mouthShape, _mouthPath);
            DrawEyebrow(_body, _eyeLeft, _eyebrowShape, _eyePaths[0]);
            DrawEyebrow(_body, _eyeRight, _eyebrowShape, _eyePaths[1]);
        }

    }

    private static void DrawMouth(Transform face, CharacterMouthShape mouthShape, PolygonPath path)
    {
        Draw.Color = Color.black;

        path.ClearAllPoints();

        var mouthIntrinsicPos = new Vector2(0f, -0.25f);

        var mouthLocalPos =
            Vector3.forward * (face.localScale.z * (0.5f + ZOffset)) +
            Vector3.up * (face.localScale.y * mouthIntrinsicPos.y);
        var mouthWorldPos = face.TransformPoint(mouthLocalPos);

        switch (mouthShape)
        {
            case CharacterMouthShape.None:
                break;
            case CharacterMouthShape.RoundOpen:
                Draw.Disc(mouthWorldPos, face.rotation, 0.1f);
                break;
            case CharacterMouthShape.TriangleOpen:
                Draw.Triangle(
                    mouthWorldPos + face.TransformDirection(new Vector3(0f, +0.1f, 0f)),
                    mouthWorldPos + face.TransformDirection(new Vector3(+0.1f, 0f, 0f)),
                    mouthWorldPos + face.TransformDirection(new Vector3(-0.1f, 0f, 0f))
                );
                break;
            case CharacterMouthShape.BigSmile:
                var localCenter = new Vector2(0, -0.33f);
                path.AddPoint(localCenter + new Vector2(-0.2f, +0.12f));
                path.AddPoint(localCenter + new Vector2(+0.2f, +0.12f));
                path.AddPoint(localCenter + new Vector2(+0.1f, -0.1f));
                path.AddPoint(localCenter + new Vector2(-0.1f, -0.1f));
                Draw.PushMatrix();
                Draw.Matrix = Matrix4x4.TRS(
                    face.TransformPoint(Vector3.forward * (face.localScale.z * (0.5f + ZOffset))),
                    face.rotation,
                    face.localScale
                );
                Draw.Polygon(
                   path
                );
                Draw.PopMatrix();
                break;
            case CharacterMouthShape.LineFlat:
                Draw.Line(
                    mouthWorldPos + face.TransformDirection(new Vector3(+0.1f, 0f, 0f)),
                    mouthWorldPos + face.TransformDirection(new Vector3(-0.1f, 0f, 0f))
                );
                break;
        }
    }

    private static void DrawEyebrow(Transform face, Transform eye, CharacterEyebrowShape shape, PolylinePath path)
    {
        // Draw.Sphere(eyeTransform.position, 0.25f);

        int side = (int)math.sign(eye.localPosition.x);

        Draw.Color = Color.black;

        path.ClearAllPoints();

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
        var localScale = face.localScale;
        localScale.x *= -side;
        Draw.Matrix = Matrix4x4.TRS(
            eye.TransformPoint(Vector3.forward * (face.localScale.z * ZOffset) + localCenter),
            face.rotation,
            localScale
        );
        Draw.Polyline(
            path
        );
        Draw.PopMatrix();
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
