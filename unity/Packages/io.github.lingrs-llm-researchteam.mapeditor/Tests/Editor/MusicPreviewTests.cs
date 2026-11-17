#nullable enable
using System;
using System.IO;
using Arknights.MapEditor.Editor;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests
{
    public sealed class MusicPreviewTests
    {
        [Test]
        public void DetailsPreviewContainsOnePassAndAppliesTheSelectedVolume()
        {
            var preview = new MusicPreview.PreviewPcm(Array.Empty<float>(),
                new[] { .2f, .4f }, new[] { .6f, .8f }, 0, singlePass: true) { Volume = .5f };
            var samples = new float[6];
            preview.Read(samples);

            Assert.That(preview.NativeLoop, Is.False);
            Assert.That(preview.SampleCount, Is.EqualTo(4));
            Assert.That(samples, Is.EqualTo(new[] { .1f, .2f, .3f, .4f, 0f, 0f }));
        }

        [Test]
        public void FullPreviewPlaysIntroOnceAndContinuesWithTheLoop()
        {
            var preview = new MusicPreview.PreviewPcm(Array.Empty<float>(),
                new[] { 0.1f, 0.2f }, new[] { 0.3f, 0.4f, 0.5f, 0.6f }, 0);
            var samples = new float[10];

            preview.Read(samples);

            Assert.That(preview.NativeLoop, Is.False);
            Assert.That(samples, Is.EqualTo(new[] { .1f, .2f, .3f, .4f, .5f, .6f, .3f, .4f, .5f, .6f }));
        }

        [Test]
        public void LoopOnlyUsesNativeLoopAndSeeksAtFrameBoundaries()
        {
            var preview = new MusicPreview.PreviewPcm(Array.Empty<float>(), Array.Empty<float>(),
                new[] { .1f, .2f, .3f, .4f }, 0);
            var samples = new float[2];

            preview.Seek(1);
            preview.Read(samples);

            Assert.That(preview.NativeLoop, Is.True);
            Assert.That(preview.SampleCount, Is.EqualTo(4));
            Assert.That(samples, Is.EqualTo(new[] { .3f, .4f }));
        }

        [Test]
        public void TransitionFadesBothChannelsAtEqualFrameGains()
        {
            var preview = new MusicPreview.PreviewPcm(new[] { .2f, .2f, .2f, .2f, .2f, .2f },
                new[] { .6f, .6f, .6f, .6f }, new[] { .8f, .8f }, 2);
            var samples = new float[8];

            preview.Read(samples);

            Assert.That(samples, Is.EqualTo(new[] { .2f, .2f, .2f, .2f, .4f, .4f, .8f, .8f }).Within(.0001f));
        }

        [TestCase("../clip.ogg")]
        [TestCase("clips/../../clip.ogg")]
        [TestCase("https://example.test/clip.ogg")]
        [TestCase("clips/non-audio.txt")]
        public void PreviewPathRejectsNonLocalOrInvalidAssets(string file)
        {
            Assert.Throws<IOException>(() => MusicPreview.ResolveLocalClip(Path.GetTempPath(), file));
        }

        [Test]
        public void PreviewPathUsesTheLocalPackage()
        {
            Assert.That(MusicPreview.ResolveLocalClip(Path.GetTempPath(), "clips/asset.ogg"),
                Is.EqualTo(Path.Combine(Path.GetTempPath(), "clips", "asset.ogg")));
        }
    }
}
