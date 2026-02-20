using System;
using UnityEngine;

namespace WormCrawlerPrototype
{
    public sealed class GrenadeExplosionFx : MonoBehaviour
    {
        [SerializeField] private string spritesResourcesPath = "FX/grenade_explosion";
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float[] frameTimes = { 0.05f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f };
        [SerializeField] private float[] sizeFractions = { 0.25f, 0.60f, 0.80f, 1.00f, 0.70f, 0.45f };
        [SerializeField] private float animationSlowdown = 1.2f;
        [SerializeField] private int sortingOrder = 40;

        private SpriteRenderer _sr;
        private float _t;
        private int _idx;
        private float _targetDiameterWorld;

        public void Configure(float targetDiameterWorld, int sortingOrderOverride)
        {
            _targetDiameterWorld = Mathf.Max(0.01f, targetDiameterWorld);
            sortingOrder = sortingOrderOverride;
        }

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null)
            {
                _sr = gameObject.AddComponent<SpriteRenderer>();
            }
            _sr.sortingOrder = sortingOrder;

            if ((frames == null || frames.Length == 0) && !string.IsNullOrEmpty(spritesResourcesPath))
            {
                frames = Resources.LoadAll<Sprite>(spritesResourcesPath);
                if (frames != null && frames.Length > 1)
                {
                    Array.Sort(frames, (a, b) => string.CompareOrdinal(a.name, b.name));
                }
            }

            _t = 0f;
            _idx = 0;
            ApplyFrame();
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0)
            {
                Destroy(gameObject);
                return;
            }

            var max = Mathf.Min(frames.Length, frameTimes != null ? frameTimes.Length : frames.Length);
            if (max <= 0)
            {
                Destroy(gameObject);
                return;
            }

            _t += Time.deltaTime;
            var slow = Mathf.Max(0.01f, animationSlowdown);
            while (_idx < max && _t >= frameTimes[_idx] * slow)
            {
                _idx++;
                ApplyFrame();
            }

            if (_idx >= max)
            {
                Destroy(gameObject);
            }
        }

        private void ApplyFrame()
        {
            if (_sr == null || frames == null || frames.Length == 0)
            {
                return;
            }

            var max = Mathf.Min(frames.Length, sizeFractions != null ? sizeFractions.Length : frames.Length);
            var frameIndex = Mathf.Clamp(_idx, 0, Mathf.Min(frames.Length - 1, max - 1));
            _sr.sprite = frames[frameIndex];
            _sr.color = Color.white;

            var desiredDiameter = _targetDiameterWorld;
            var frac = sizeFractions != null && sizeFractions.Length > frameIndex ? Mathf.Max(0.01f, sizeFractions[frameIndex]) : 1f;
            desiredDiameter *= frac;

            var spriteDiameter = 1f;
            if (_sr.sprite != null)
            {
                var b = _sr.sprite.bounds;
                spriteDiameter = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.y));
            }

            var s = desiredDiameter / spriteDiameter;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
