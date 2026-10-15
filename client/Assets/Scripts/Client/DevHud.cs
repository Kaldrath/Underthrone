using Underthrone.Shared.Sim;
using UnityEngine;

namespace Underthrone.Client
{
    /// Throwaway developer HUD (IMGUI) for the combat slice: bars, state and net stats.
    /// Replace with the real UI once the feel is right.
    [DisallowMultipleComponent]
    public sealed class DevHud : MonoBehaviour
    {
        [SerializeField] private NetClient _net;
        [SerializeField] private LocalPlayer _player;

        private GUIStyle _label;

        private void OnGUI()
        {
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };
            GUI.color = Color.white;
            if (!_net.IsConnected || !_player.HasBody)
            {
                GUI.Label(new Rect(16, 16, 600, 24), "Connecting to the Underthrone server...", _label);
                return;
            }

            PlayerState s = _player.State;
            Bar(new Rect(16, Screen.height - 56, 260, 14), s.Health / (float)CombatTuning.PLAYER_MAX_HEALTH, new Color(0.7f, 0.15f, 0.12f));
            Bar(new Rect(16, Screen.height - 36, 260, 10), s.Stamina / CombatTuning.PLAYER_MAX_STAMINA, new Color(0.35f, 0.65f, 0.3f));
            GUI.Label(new Rect(Screen.width / 2f - 2, Screen.height / 2f - 10, 20, 20), "+", _label);

            GUI.Label(new Rect(16, 16, 600, 22),
                $"#{_net.PlayerId}  {s.Action}  RTT {_net.RoundTripMs} ms  tick {_net.LastServerTick}  correction {_player.LastCorrection:F2} m",
                _label);
            GUI.Label(new Rect(16, 36, 600, 22), "WASD move  mouse look  LMB attack  RMB block (tap just before a hit to parry)  Space dodge  Esc release cursor", _label);
            for (int lineIndex = 0; lineIndex < _net.CombatLog.Count; lineIndex++)
            {
                GUI.Label(new Rect(Screen.width - 320, 16 + lineIndex * 20, 310, 20), _net.CombatLog[lineIndex], _label);
            }
        }

        private static void Bar(Rect rect, float fill, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
