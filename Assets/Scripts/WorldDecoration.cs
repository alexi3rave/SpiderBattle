using UnityEngine;

namespace WormCrawlerPrototype
{
    public sealed class WorldDecoration : MonoBehaviour
    {
        public enum SizeCategory
        {
            Small,
            Medium,
            Large,
        }

        public SizeCategory size;
        public float minSlopeDeg = 0f;
        public float maxSlopeDeg = 45f;
        public float verticalOffset = 0f;
    }
}
