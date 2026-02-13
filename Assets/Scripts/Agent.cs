
using Unity.Mathematics;
using UnityEngine;

namespace Frantic.DesktopPets
{
    public class Agent : MonoBehaviour
    {
        public GameObject Root;
        public BoxCollider Collider;

        private Vector3 _targetDirection;
        private Vector3 _filteredTargetDirection;
        private Vector3 _targetVelocity;
        private Quaternion _bodyTargetRotation;

        public float MoveSpeed = 3f;

        public bool CanMove = true;

        public void Move(Vector3 direction)
        {
            _targetDirection = Vector3.ClampMagnitude(direction, 1f);
        }

        public void RotateTo(Quaternion rotation)
        {
            _bodyTargetRotation = rotation;
        }

        private void Update()
        {

            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 2))
            {
                // Debug.Log($"floating on: {hit.collider.name}");
                transform.position = hit.point + Vector3.up * 0.66f;
            }

            if (!CanMove)
            {
                return;
            }


            const float charMoveSpeed = 2f;

            Quaternion lookRot;
            if (_targetDirection.magnitude > 0.25f)
            {
                lookRot = Quaternion.LookRotation(_targetDirection);
            } else
            {
                lookRot = _bodyTargetRotation;
            }
            
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, 4f * Time.deltaTime);

            if (math.dot(transform.forward, _targetDirection.normalized) < 0.5f)
            {
                // Wait until we're looking roughly in the target direction before actually walking there
                return;
            }

            // move
            transform.position += _targetDirection * (charMoveSpeed * Time.deltaTime);
        }
    }
}
