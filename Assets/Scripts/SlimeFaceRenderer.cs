using Shapes;
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

public class SlimeFaceRenderer : ImmediateModeShapeDrawer
{
    [SerializeField] private Material _material;

    private Transform _transform;

    private CharacterMouthShape _mouthShape;
    private CharacterEyebrowShape _eyebrowShape;


    private double _lastBlinkTime = -1;
    private float _blinkDuration = 3;

    private PolygonPath _mouthPath;
    private PolylinePath[] _eyePaths;

    private Rng _rng;

    [SerializeField] private RenderTexture _faceTex;
    [SerializeField] private Camera _faceCam;

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

        // _faceTex = new RenderTexture(1024, 1024, 32, RenderTextureFormat.ARGB32);
        // _faceTex.Create();

        // var faceCamObj = new GameObject("FaceCam");
        // _faceCam = faceCamObj.AddComponent<Camera>();
        // _faceCam.clearFlags = CameraClearFlags.SolidColor;
        // _faceCam.backgroundColor = new Color(0,0,0,0);
        // _faceCam.orthographic = true;
        // _faceCam.orthographicSize = 1;
        // _faceCam.targetTexture = _faceTex;
        // _faceCam.gameObject.SetActive(false);
        // faceCamObj.AddComponent<UniversalAdditionalCameraData>();

        // _material.SetTexture("FaceTexture", _faceTex);

        _faceCam.Render();
    }

    private void OnDestroy()
    {
        if (_faceTex.IsCreated())
        {
            Destroy(_faceTex);
        }
    }

    private const float ZOffset = 0.01f;

    public override void DrawShapes(Camera cam)
    {
        _faceCam.transform.position = new Vector3(0, -100, -1);

        using (Draw.Command(cam, UnityEngine.Rendering.Universal.RenderPassEvent.AfterRenderingOpaques))
        {
            Draw.ThicknessSpace = ThicknessSpace.Meters;
            Draw.RadiusSpace = ThicknessSpace.Meters;
            Draw.Thickness = 0.02f;
            Draw.BlendMode = ShapesBlendMode.Opaque;

            Draw.Matrix = Matrix4x4.TRS(
                new Vector3(0, -100, 0),
                Quaternion.identity,
                new Vector3(1,1,1)
            );

            Draw.Color = Color.black;
            Draw.Cube(0.5f);

            // DrawMouth(_body, _mouthShape, _mouthPath);
            // DrawEyebrow(_body, _eyeLeft, _eyebrowShape, _eyePaths[0]);
            // DrawEyebrow(_body, _eyeRight, _eyebrowShape, _eyePaths[1]);
        }
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
