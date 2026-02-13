using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using Rng = Unity.Mathematics.Random;

namespace Frantic.DesktopPets
{
    [RequireComponent(typeof(Agent))]
    public class NPC : MonoBehaviour
    {
        private Agent _agent;

        private NavMeshPath _navPath;
        public const int AreaMask = 0xffff;

        int _navPathIdx;
        NavState _navState;


        Coroutine _moveRoutine;

        private Rng _rng;

        public Agent Agent
        {
            get => _agent;
        }

        public NavState NavigationState
        {
            get => _navState;
        }

        public NavMeshPath NavPath
        {
            get => _navPath;
        }

        public int NavPathIdx
        {
            get => _navPathIdx;
        }

        float _lastNodeDistance;

        private void Awake()
        {
            _rng = new Rng(320853);

            _navPath = new NavMeshPath();
            _agent = gameObject.GetComponent<Agent>();
            NavMesh.CalculatePath(_agent.transform.position, _agent.transform.position, AreaMask, _navPath);
        }

        public Vector3 GetPathEnd()
        {
            var corners = _navPath.corners;
            var pathEnd = corners.Length > 0 ? corners[^1] : _agent.transform.position;
            return pathEnd;
        }

        // Can be used as fire-and-forget behaviour, or yielded as a coroutine
        public Coroutine MoveTo(Vector3 position)
        {
            if (_moveRoutine != null)
            {
                StopCoroutine(_moveRoutine);
            }
            return StartCoroutine(MoveToAsync(position));
        }

        private IEnumerator MoveToAsync(Vector3 position)
        {
            NavMeshHit navMeshHit;
            if (NavMesh.SamplePosition(position, out navMeshHit, 2f, AreaMask))
            {
                NavMesh.CalculatePath(transform.position, navMeshHit.position, AreaMask, _navPath);
                _navPathIdx = 0;
            }
            else
            {
                // Error
                Debug.LogWarning($"NPC {gameObject.name} trying to move to inaccessible location {position}");
                _navState = NavState.Error;
                yield break;
            }

            float _stuckTimer = 0;
            _navState = NavState.Moving;

            const float navNodeDistanceThreshold = 0.5f;

            while (_navState == NavState.Moving)
            {
                if (!_agent.CanMove)
                {
                    yield return new WaitForEndOfFrame();
                }

                // Moving along nav path
                var nodes = _navPath.corners;
                if (nodes != null && _navPathIdx < nodes.Length)
                {
                    Vector3 moveTarget = nodes[_navPathIdx];
                    Vector3 groundPos = transform.position;
                    Vector3 moveDelta = moveTarget - groundPos;

                    var velocity = moveDelta.normalized;

                    _agent.Move(velocity);

                    float nodeDistance = Vector3.Distance(groundPos, moveTarget);
                    if (nodeDistance >= _lastNodeDistance)
                    {
                        _stuckTimer += Time.deltaTime;
                    }
                    else
                    {
                        _stuckTimer = 0;
                    }
                    _lastNodeDistance = nodeDistance;

                    if (_stuckTimer > 1f)
                    {
                        _navState = NavState.Error;
                        break;
                    }

                    Debug.Log(nodeDistance);

                    if (nodeDistance < navNodeDistanceThreshold)
                    {
                        Debug.Log($"Reached waypoint {_navPathIdx}/{nodes.Length}");
                        if (_navPathIdx + 1 < nodes.Length)
                        {
                            _lastNodeDistance = Vector3.Distance(groundPos, nodes[_navPathIdx + 1]);
                        }
                        _navPathIdx++;
                    }
                    if (_navPathIdx >= nodes.Length)
                    {
                        Debug.Log("Reached destination!");
                        _navState = NavState.Arrived;
                        break;
                    }

                    // Todo: if waypoint unreachable, resort to fallback behavior
                }

                yield return new WaitForEndOfFrame();
            }

            if (_navState == NavState.Error)
            {
                _navPath.ClearCorners();
                _navPathIdx = 0;
            }

            _agent.Move(Vector3.zero);
        }

        public enum Direction
        {
            Forward = +1,
            Reverse = -1
        }

        public enum NavState
        {
            Arrived,
            Moving,
            Error
        }
    }
}