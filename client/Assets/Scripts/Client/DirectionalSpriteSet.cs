using UnityEngine;

namespace Underthrone.Client
{
    /// The 8 viewing angles of one animation frame, in the order the camera sees them:
    /// 0 S (facing the viewer), 1 SW, 2 W, 3 NW, 4 N (back), 5 NE, 6 E, 7 SE.
    /// This is the same order as the Aseprite direction tags. With <see cref="MirrorEastFromWest"/>
    /// only S, SW, W, NW and N need drawing; NE/E/SE reuse NW/W/SW flipped (Daggerfall's trick).
    [CreateAssetMenu(menuName = "Underthrone/Directional Sprite Set")]
    public sealed class DirectionalSpriteSet : ScriptableObject
    {
        [SerializeField] private Sprite[] _sprites = new Sprite[8];
        [SerializeField] private bool _mirrorEastFromWest = true;

        public bool MirrorEastFromWest => _mirrorEastFromWest;

        public Sprite Get(int orientation, out bool flipX)
        {
            flipX = false;
            if (_mirrorEastFromWest && orientation >= 5)
            {
                flipX = true;
                orientation = 8 - orientation; // NE->NW, E->W, SE->SW
            }
            return _sprites[orientation];
        }

        /// Runtime construction (used for generated placeholders).
        public static DirectionalSpriteSet Create(Sprite[] eight, bool mirrorEastFromWest)
        {
            var set = CreateInstance<DirectionalSpriteSet>();
            set._sprites = eight;
            set._mirrorEastFromWest = mirrorEastFromWest;
            return set;
        }
    }
}
