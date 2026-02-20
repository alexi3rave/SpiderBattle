#if false
using NUnit.Framework;
using UnityEngine;

namespace WormCrawlerPrototype.Tests.EditMode
{
    public sealed class LevelGeneratorEditModeTests
    {
        [Test]
        public void LevelGenerator_IsDeterministic()
        {
            var gen = new LevelGenerator();

            var settings = new LevelGenerator.Settings
            {
                Seed = 12345,
                Theme = LevelTheme.Railway,
                WidthTiles = 80,
                HeightTiles = 40,
            };

            var a = gen.GenerateData(settings);
            var b = gen.GenerateData(settings);

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.AreEqual(a.Config.Seed, b.Config.Seed);
            Assert.AreEqual(a.Config.Theme, b.Config.Theme);
            Assert.AreEqual(a.Config.WidthTiles, b.Config.WidthTiles);
            Assert.AreEqual(a.Config.HeightTiles, b.Config.HeightTiles);
            Assert.AreEqual(a.Ground.SurfaceHeights.Length, b.Ground.SurfaceHeights.Length);
        }

        [Test]
        public void LevelGenerator_PathExists_MostOfTheTime()
        {
            var gen = new LevelGenerator();

            var ok = 0;
            var tries = 10;
            for (var i = 0; i < tries; i++)
            {
                var settings = new LevelGenerator.Settings
                {
                    Seed = 1000 + i,
                    Theme = LevelTheme.Railway,
                    WidthTiles = 80,
                    HeightTiles = 40,
                };

                var data = gen.GenerateData(settings);
                if (data != null && data.PathValid)
                {
                    ok++;
                }
            }

            Assert.GreaterOrEqual(ok, tries / 2);
        }
    }
}
#endif
