using UnityEngine;

namespace Underthrone.Client
{
    /// Daggerfall-style billboard: the sprite always turns to face the camera around the vertical
    /// axis, and shows whichever of 8 drawn angles matches where the camera stands relative to
    /// the unit's facing.
    ///
    /// Orientation math adapted from Daggerfall Unity (DaggerfallMobileUnit.UpdateOrientation),
    /// MIT License, Copyright (c) 2009-2023 Daggerfall Workshop.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class Billboard8Dir : MonoBehaviour
    {
        private const int ORIENTATIONS = 8;
        private const float DEGREES_PER_ORIENTATION = 360f / ORIENTATIONS;

        [SerializeField] private DirectionalSpriteSet _sprites;

        private SpriteRenderer _renderer;
        private Transform _camera;
        private float _facingYawDegrees;
        private int _lastOrientation = -1;

        public int Orientation => _lastOrientation;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
        }

        public void SetSprites(DirectionalSpriteSet sprites)
        {
            _sprites = sprites;
            _lastOrientation = -1;
        }

        public void SetCamera(Transform cameraTransform) => _camera = cameraTransform;

        /// World yaw the unit is facing (degrees, 0 = +Z).
        public void SetFacing(float yawDegrees) => _facingYawDegrees = yawDegrees;

        public void SetTint(Color color) => _renderer.color = color;

        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }
            Vector3 toCamera = _camera.position - transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 1e-6f)
            {
                return;
            }
            // Face the camera on the vertical axis only, like Daggerfall's flats.
            transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);

            int orientation = ComputeOrientation(toCamera.normalized, Quaternion.Euler(0f, _facingYawDegrees, 0f) * Vector3.forward);
            if (orientation == _lastOrientation || _sprites == null)
            {
                return;
            }
            _lastOrientation = orientation;
            _renderer.sprite = _sprites.Get(orientation, out bool flip);
            _renderer.flipX = flip;
        }

        /// 0 = camera in front of the unit (S), then SW, W, NW, N, NE, E, SE.
        public static int ComputeOrientation(Vector3 toCameraFlat, Vector3 unitForwardFlat)
        {
            unitForwardFlat.y = 0f;
            float angle = Vector3.Angle(toCameraFlat, unitForwardFlat);
            angle *= -Mathf.Sign(Vector3.Cross(toCameraFlat, unitForwardFlat).y);
            int orientation = -Mathf.RoundToInt(angle / DEGREES_PER_ORIENTATION);
            return (orientation + ORIENTATIONS) % ORIENTATIONS;
        }
    }
}
