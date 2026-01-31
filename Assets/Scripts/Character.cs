using System;
using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class Character : MonoBehaviour
{
    [SerializeField] private Transform _leftEye;
    [SerializeField] private Transform _rightEye;

    private Transform _transform;

    private Vector3 _lookTarget = new Vector3(0,0,-1);
    private float _jumpTimer = -1;

    private double _lastBlinkTime = -1;

    private void Awake()
    {
        _transform = gameObject.GetComponent<Transform>();
    }

    public void LookAt(Vector3 target)
    {
        _lookTarget = target;
    }

    public void Jump()
    {
        Debug.Log("Jump!");
        _jumpTimer = 0;
    }

    // Update is called once per frame
    void Update()
    {
        if (Time.timeAsDouble >= _lastBlinkTime + 3f)
        {
            StartCoroutine(BlinkAsync());
            _lastBlinkTime = Time.timeAsDouble;
        }


        // _transform.Rotate(Vector3.up, 45f * Time.deltaTime);
        _transform.LookAt(_lookTarget);

        // _transform.localScale = Vector3.one * (3f + (float)math.sin(Time.timeAsDouble * math.PI2 * 0.5f));

        if (_jumpTimer >= 0)
        {
            const float jumpDur = 0.66f;
            float jumpLerp = math.saturate(_jumpTimer / jumpDur);
            float y = math.sin(jumpLerp * math.PI) * 2f;
            _transform.position = new Vector3(0, y, 0);

            _jumpTimer += Time.deltaTime;

            if (_jumpTimer >= jumpDur)
            {
                _jumpTimer = -1;
            }
        }
    }

    private IEnumerator BlinkAsync()
    {
        float eyeOpenScale = 0.2f;

        float time = 0;
        const float blinkDur = 0.2f;
        Vector3 eyeScale;
        while (time < blinkDur)
        {
            float blinkLerp = math.saturate(time / blinkDur);
            float scale = eyeOpenScale * math.cos(blinkLerp * math.PI2);
            eyeScale = Vector3.one * scale;
            _leftEye.localScale = eyeScale;
            _rightEye.localScale = eyeScale;

            time += Time.deltaTime;
            yield return new WaitForEndOfFrame();
        }

        eyeScale = Vector3.one * eyeOpenScale;
        _leftEye.localScale = eyeScale;
        _rightEye.localScale = eyeScale;
    }
}
