using System.Collections.Generic;
using Underthrone.Shared.Client;
using Underthrone.Shared.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Underthrone.Client
{
    /// First-person body of the local player. Reads input, runs the shared simulation at the
    /// server's tick rate for instant response (prediction), and reconciles with the server.
    [DisallowMultipleComponent]
    public sealed class LocalPlayer : MonoBehaviour
    {
        private const float EYE_HEIGHT = 1.6f;
        private const int INPUT_REDUNDANCY = 4;
        private const float CORRECTION_SMOOTHING = 12f;

        [SerializeField] private NetClient _net;
        [SerializeField] private Transform _eyes;
        [SerializeField] private float _mouseSensitivity = 0.12f;

        private readonly List<PlayerInput> _recentInputs = new List<PlayerInput>(16);
        private ClientPrediction _prediction;
        private float _accumulator;
        private uint _inputTick;
        private float _yaw;
        private float _pitch;
        private bool _attackLatched;
        private bool _dodgeLatched;
        private Vector3 _visualOffset;
        private float _shake;

        public bool HasBody => _prediction != null;
        public PlayerState State => _prediction != null ? _prediction.State : default;
        public float LastCorrection => _prediction != null ? _prediction.LastCorrection : 0f;

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            ReadLook();
            LatchButtons();
            if (_prediction == null)
            {
                return;
            }

            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= SimConstants.TICK_DELTA && steps < 5)
            {
                _accumulator -= SimConstants.TICK_DELTA;
                StepOnce();
                steps++;
            }
            if (steps == 5)
            {
                _accumulator = 0f;
            }

            _visualOffset = Vector3.Lerp(_visualOffset, Vector3.zero, 1f - Mathf.Exp(-CORRECTION_SMOOTHING * Time.deltaTime));
            _shake = Mathf.MoveTowards(_shake, 0f, Time.deltaTime * 4f);
            ApplyCamera();
        }

        /// Called by NetClient for every snapshot.
        public void OnServerState(uint ackInputTick, in PlayerState serverState)
        {
            if (_prediction == null)
            {
                _prediction = new ClientPrediction(serverState);
                _yaw = serverState.Yaw * Mathf.Rad2Deg;
                return;
            }
            Vector3 before = _prediction.State.Position.ToUnity();
            _prediction.Reconcile(ackInputTick, serverState);
            // Hide small corrections by easing the camera across them instead of snapping.
            Vector3 after = _prediction.State.Position.ToUnity();
            if ((before - after).sqrMagnitude < 4f)
            {
                _visualOffset += before - after;
            }
        }

        public void OnStruck(HitOutcome outcome, int damage)
        {
            _shake = outcome == HitOutcome.Hit || outcome == HitOutcome.GuardBroken ? 1f : 0.35f;
        }

        public void OnLandedHit()
        {
            _shake = Mathf.Max(_shake, 0.25f);
        }

        private void ReadLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }
            Vector2 delta = mouse.delta.ReadValue() * _mouseSensitivity;
            _yaw = Mathf.Repeat(_yaw + delta.x, 360f);
            _pitch = Mathf.Clamp(_pitch - delta.y, -80f, 80f);
        }

        /// Presses between ticks are latched so a quick click is never lost.
        private void LatchButtons()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                _attackLatched = true;
            }
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                _dodgeLatched = true;
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _attackLatched = false;
            }
        }

        private void StepOnce()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            float moveX = 0f;
            float moveY = 0f;
            if (keyboard != null)
            {
                moveX = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                moveY = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            }

            InputButtons buttons = InputButtons.None;
            if (_attackLatched || (mouse != null && mouse.leftButton.isPressed))
            {
                buttons |= InputButtons.Attack;
            }
            if (_dodgeLatched || (keyboard != null && keyboard.spaceKey.isPressed))
            {
                buttons |= InputButtons.Dodge;
            }
            if (mouse != null && mouse.rightButton.isPressed)
            {
                buttons |= InputButtons.Block;
            }
            _attackLatched = false;
            _dodgeLatched = false;

            PlayerInput input = PlayerInput.Create(++_inputTick, moveX, moveY, _yaw * Mathf.Deg2Rad, buttons);
            _prediction.Predict(input);

            _recentInputs.Add(input);
            if (_recentInputs.Count > INPUT_REDUNDANCY)
            {
                _recentInputs.RemoveAt(0);
            }
            _net.SendInputs(_recentInputs, 0, _recentInputs.Count);
        }

        private void ApplyCamera()
        {
            PlayerState s = _prediction.State;
            Vector3 position = s.Position.ToUnity() + _visualOffset + Vector3.up * EYE_HEIGHT;
            float roll = 0f;
            if (_shake > 0f)
            {
                position += Random.insideUnitSphere * (_shake * 0.04f);
                roll = Random.Range(-1f, 1f) * _shake * 1.5f;
            }
            if (s.Action == PlayerAction.Dodging)
            {
                position.y -= 0.25f; // duck through the dodge
            }
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, _yaw, 0f));
            _eyes.localRotation = Quaternion.Euler(_pitch, 0f, roll);
        }
    }
}
