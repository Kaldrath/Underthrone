using System.Collections.Generic;
using Underthrone.Shared.Client;
using Underthrone.Shared.Protocol;
using Underthrone.Shared.Sim;
using UnityEngine;

namespace Underthrone.Client
{
    /// Draws every entity the server reports except the local player, as interpolated
    /// 8-direction billboards. Remote state is never simulated here, only displayed.
    [DisallowMultipleComponent]
    public sealed class RemoteEntities : MonoBehaviour
    {
        /// Remote entities are drawn this far in the past so there are always two snapshots to
        /// blend between (3 ticks at 30 Hz). Must match the server's CLIENT_INTERP_TICKS.
        private const double INTERP_DELAY = 3.0 / SimConstants.TICK_RATE;
        private const float FLASH_SECONDS = 0.12f;

        [SerializeField] private NetClient _net;
        [SerializeField] private Transform _camera;

        private sealed class View
        {
            public GameObject Root;
            public Billboard8Dir Billboard;
            public SnapshotInterpolator Interp;
            public EntityKind Kind;
            public float FlashUntil;
            public Color FlashColor;
            public int SeenFrame;
        }

        private readonly Dictionary<ushort, View> _views = new Dictionary<ushort, View>();
        private readonly List<ushort> _stale = new List<ushort>();
        private DirectionalSpriteSet _playerSprites;
        private DirectionalSpriteSet _ghoulSprites;
        private int _snapshotFrame;

        private void Awake()
        {
            _playerSprites = PlaceholderSprites.Build(new Color32(150, 160, 175, 255), new Color32(90, 96, 110, 255), new Color32(30, 30, 36, 255));
            _ghoulSprites = PlaceholderSprites.Build(new Color32(115, 133, 92, 255), new Color32(59, 66, 52, 255), new Color32(231, 202, 126, 255));
        }

        public void OnSnapshot(double serverTime, List<EntitySnapshot> entities, ushort localId)
        {
            _snapshotFrame++;
            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++)
            {
                EntitySnapshot e = entities[entityIndex];
                if (e.Id == localId)
                {
                    continue;
                }
                if (!_views.TryGetValue(e.Id, out View view))
                {
                    view = CreateView(e);
                    _views.Add(e.Id, view);
                }
                view.Interp.Add(serverTime, e);
                view.SeenFrame = _snapshotFrame;
            }

            // Anything the server stopped reporting is gone.
            _stale.Clear();
            foreach (KeyValuePair<ushort, View> pair in _views)
            {
                if (pair.Value.SeenFrame != _snapshotFrame)
                {
                    _stale.Add(pair.Key);
                }
            }
            for (int staleIndex = 0; staleIndex < _stale.Count; staleIndex++)
            {
                Remove(_stale[staleIndex]);
            }
        }

        public void OnCombatEvent(ushort targetId, HitOutcome outcome)
        {
            if (!_views.TryGetValue(targetId, out View view))
            {
                return;
            }
            view.FlashUntil = Time.time + FLASH_SECONDS;
            view.FlashColor = outcome == HitOutcome.Hit ? Color.white : new Color(1f, 0.85f, 0.4f);
        }

        public void Remove(ushort id)
        {
            if (_views.TryGetValue(id, out View view))
            {
                Destroy(view.Root);
                _views.Remove(id);
            }
        }

        private void Update()
        {
            double renderTime = _net.ServerTimeNow - INTERP_DELAY;
            foreach (KeyValuePair<ushort, View> pair in _views)
            {
                View view = pair.Value;
                if (!view.Interp.HasData)
                {
                    continue;
                }
                EntitySnapshot s = view.Interp.Sample(renderTime, out float yaw);
                view.Root.transform.position = new Vector3(s.X, 0f, s.Z);
                view.Billboard.SetFacing(yaw * Mathf.Rad2Deg);
                view.Billboard.SetTint(Time.time < view.FlashUntil ? view.FlashColor : TintFor(view.Kind, s.Action));
            }
        }

        private View CreateView(in EntitySnapshot e)
        {
            var root = new GameObject($"{e.Kind} #{e.Id}");
            root.transform.SetParent(transform, false);
            var sprite = new GameObject("Sprite");
            sprite.transform.SetParent(root.transform, false);
            sprite.AddComponent<SpriteRenderer>();
            Billboard8Dir billboard = sprite.AddComponent<Billboard8Dir>();
            billboard.SetSprites(e.Kind == EntityKind.Ghoul ? _ghoulSprites : _playerSprites);
            billboard.SetCamera(_camera);
            return new View { Root = root, Billboard = billboard, Interp = new SnapshotInterpolator(), Kind = e.Kind };
        }

        /// Readability first: the telegraph and the punish window must be obvious at a glance.
        private static Color TintFor(EntityKind kind, byte action)
        {
            if (kind == EntityKind.Ghoul)
            {
                switch ((EnemyAction)action)
                {
                    case EnemyAction.Windup:
                        return new Color(1f, 0.55f, 0.35f);   // telegraph: warm warning tint
                    case EnemyAction.Active:
                        return new Color(1f, 0.3f, 0.25f);
                    case EnemyAction.Staggered:
                        return new Color(0.55f, 0.6f, 0.75f); // punish window
                    case EnemyAction.Dead:
                        return new Color(0.2f, 0.2f, 0.2f, 0.35f);
                    default:
                        return Color.white;
                }
            }
            switch ((PlayerAction)action)
            {
                case PlayerAction.Blocking:
                    return new Color(0.75f, 0.85f, 1f);
                case PlayerAction.Dodging:
                    return new Color(1f, 1f, 1f, 0.6f);
                case PlayerAction.Dead:
                    return new Color(0.2f, 0.2f, 0.2f, 0.35f);
                default:
                    return Color.white;
            }
        }
    }
}
