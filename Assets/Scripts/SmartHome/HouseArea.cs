using UnityEngine;

namespace SmartHouse
{
    /// <summary>The inside of the house, taken from the walls' bounds: x -5..5, z -4..4, floor to ceiling.</summary>
    public static class HouseArea
    {
        static Bounds? bounds;

        public static Bounds Bounds
        {
            get
            {
                if (bounds == null)
                {
                    var walls = GameObject.Find("Static_Walls");
                    var b = walls != null && walls.TryGetComponent(out Renderer r) ? r.bounds : new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(10f, 3f, 8f));
                    b.Expand(-0.1f); // the outer skin of the walls doesn't count as inside
                    bounds = b;
                }
                return bounds.Value;
            }
        }

        public static bool Contains(Vector3 point) => Bounds.Contains(point);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => bounds = null;
    }
}
