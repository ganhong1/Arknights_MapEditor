#nullable enable
using UnityEngine;
using static Arknights.MapEditor.Catalog.OfflineArtRecord;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class OfflineArtCameraDemo
    {
        internal float Duration { get; }
        internal Vector3 Offset { get; }
        internal bool ReferenceOffset { get; }
        internal bool FocusCompensation { get; }
        internal bool Dither { get; }

        private OfflineArtCameraDemo(float duration, Vector3 offset, bool reference, bool focus, bool dither)
        {
            Duration = Mathf.Clamp(duration, 0.05f, 30f);
            Offset = offset;
            ReferenceOffset = reference;
            FocusCompensation = focus;
            Dither = dither;
        }

        internal static OfflineArtCameraDemo From(OfflineArtRecord record)
        {
            if (record.Type == "CommonDitherCameraPlugin")
                return new OfflineArtCameraDemo(1f, Vector3.zero, false, false, true);

            var duration = Number(record.Fields["move_time"], Number(record.Constants?["MOVE_TIME"], 1f));
            var offset = record.Fields["_originOffset"];
            var position = offset == null ? new Vector3(-7f, 0f, 0f) :
                new Vector3(Number(offset["x"]), Number(offset["y"]), Number(offset["z"]));
            return new OfflineArtCameraDemo(duration, position, offset == null,
                Enabled(record.Fields["_compensateFocusByLookAtShift"]), false);
        }

        internal Vector3 OffsetAt(float time) => Vector3.Lerp(Offset, Vector3.zero, Mathf.SmoothStep(0f, 1f, time / Duration));
    }
}
