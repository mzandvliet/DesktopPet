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
    [SerializeField] private SkinnedMeshRenderer _faceRenderer;
    [SerializeField] private Color _baseColor = Color.ghostWhite;
    [SerializeField] private Color _innerColor = Color.aquamarine;

    private Material _material;
    private Transform _transform;
    
    public float Blink
    {
        get; set;
    }


    private Rng _rng;

    public Transform Transform
    {
        get => _transform;
    }


    private void Awake()
    {
        _rng = new Rng(1234);
        _transform = gameObject.GetComponent<Transform>();
        _material = _faceRenderer.sharedMaterial;
        _material.SetColor("_BaseColor", _baseColor);
        _material.SetColor("_InnerColor", _innerColor);
    }


    private void LateUpdate()
    {
        _material.SetFloat("_Blink", Blink);
    }

    public override void DrawShapes(Camera cam)
    {
        
    }

    public const int CharacterMouthShapeMax = 4;
    public enum MouthShape
    {
        None,
        RoundOpen,
        TriangleOpen,
        BigSmile,
        LineFlat
    }

    public const int CharacterEyebrowShapeMax = 5;
    public enum EyebrowShape
    {
        None,
        Neutral,
        Raised,
        Concerned,
        Angry,
    }
}
