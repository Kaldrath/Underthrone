using UnityEngine;
using UnityEngine.UI;

namespace Underthrone.Client
{
    /// Daggerfall Unity's retro mode, simplified: the world camera renders into a low-resolution
    /// point-filtered texture which is scaled up to fill the screen, giving chunky pixels at any
    /// window size. Works with the built-in renderer and URP alike.
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class RetroCamera : MonoBehaviour
    {
        [SerializeField] private int _verticalResolution = 270;

        private Camera _world;
        private RenderTexture _target;
        private RawImage _image;
        private int _screenWidth;
        private int _screenHeight;

        private void Awake()
        {
            _world = GetComponent<Camera>();

            // A camera that renders nothing but owns the screen, so the overlay UI always has
            // somewhere to draw.
            var presentGo = new GameObject("RetroPresent");
            Camera present = presentGo.AddComponent<Camera>();
            present.clearFlags = CameraClearFlags.SolidColor;
            present.backgroundColor = Color.black;
            present.cullingMask = 0;
            present.depth = _world.depth + 10;

            var canvasGo = new GameObject("RetroCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100;
            var imageGo = new GameObject("RetroImage");
            imageGo.transform.SetParent(canvasGo.transform, false);
            _image = imageGo.AddComponent<RawImage>();
            _image.raycastTarget = false;
            RectTransform rect = _image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            if (Screen.width == _screenWidth && Screen.height == _screenHeight && _target != null)
            {
                return;
            }
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.targetTexture = null;
            }
            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
            }
        }

        private void Rebuild()
        {
            int height = Mathf.Max(90, _verticalResolution);
            int width = Mathf.Max(160, Mathf.RoundToInt(height * (_screenWidth / (float)Mathf.Max(1, _screenHeight))));
            if (_target != null)
            {
                _world.targetTexture = null;
                _target.Release();
                Destroy(_target);
            }
            _target = new RenderTexture(width, height, 24)
            {
                filterMode = FilterMode.Point,
                name = "RetroTarget",
            };
            _target.Create();
            _world.targetTexture = _target;
            _image.texture = _target;
        }
    }
}
