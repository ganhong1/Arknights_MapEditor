#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    public sealed partial class LevelDocument
    {
        private void ValidateRouteAndWaveStructure(List<LevelDiagnostic> diagnostics)
        {
            foreach (var library in new[] { "routes", "extraRoutes" })
            {
                if (!(root[library] is JArray routes)) continue;
                foreach (var routeToken in routes)
                {
                    if (!(routeToken is JObject route))
                    {
                        diagnostics.Add(new LevelDiagnostic("error", routeToken.Path, "Expected route object"));
                        continue;
                    }
                    EnumDiagnostic(route["motionMode"], route.Path + ".motionMode", new[] { "WALK", "FLY" }, "E_NUM", diagnostics);
                    PositionDiagnostic(route["startPosition"], route.Path + ".startPosition", diagnostics);
                    PositionDiagnostic(route["endPosition"], route.Path + ".endPosition", diagnostics);
                    if (route["checkpoints"] == null || route["checkpoints"]!.Type == JTokenType.Null) continue;
                    if (!(route["checkpoints"] is JArray checkpoints))
                    {
                        diagnostics.Add(new LevelDiagnostic("error", route.Path + ".checkpoints", "Expected checkpoints array"));
                        continue;
                    }
                    foreach (var checkpointToken in checkpoints)
                    {
                        if (!(checkpointToken is JObject checkpoint))
                        {
                            diagnostics.Add(new LevelDiagnostic("error", checkpointToken.Path, "Expected checkpoint object"));
                            continue;
                        }
                        EnumDiagnostic(checkpoint["type"], checkpoint.Path + ".type", new[] { "MOVE", "WAIT_FOR_SECONDS", "WAIT_FOR_PLAY_TIME", "WAIT_CURRENT_FRAGMENT_TIME", "WAIT_CURRENT_WAVE_TIME", "DISAPPEAR", "APPEAR_AT_POS", "ALERT", "PATROL_MOVE", "WAIT_BOSSRUSH_WAVE", "MAP_OFFSET_MOVE" }, "INVALID", diagnostics);
                        if (checkpoint["position"] != null && checkpoint["position"]!.Type != JTokenType.Null)
                            PositionDiagnostic(checkpoint["position"], checkpoint.Path + ".position", diagnostics);
                    }
                }
            }
            if (root["waves"] is JArray waves)
                foreach (var wave in waves)
                {
                    if (!(wave is JObject item)) continue;
                    NumberDiagnostic(item["preDelay"], item.Path + ".preDelay", 0, false, diagnostics);
                    NumberDiagnostic(item["postDelay"], item.Path + ".postDelay", 0, false, diagnostics);
                    NumberDiagnostic(item["maxTimeWaitingForNextWave"], item.Path + ".maxTimeWaitingForNextWave", -1, false, diagnostics);
                    if (item["fragments"] is JArray fragments)
                        foreach (var fragment in fragments)
                            if (fragment is JObject part) NumberDiagnostic(part["preDelay"], part.Path + ".preDelay", 0, false, diagnostics);
                }
            if (root["branches"] is JObject branches)
                foreach (var property in branches.Properties())
                    if (property.Value is JObject branch && branch["phases"] is JArray phases)
                        foreach (var phase in phases)
                            if (phase is JObject part) NumberDiagnostic(part["preDelay"], part.Path + ".preDelay", 0, false, diagnostics);
        }

        private static void PositionDiagnostic(JToken? token, string path, List<LevelDiagnostic> diagnostics)
        {
            if (!(token is JObject position))
            {
                diagnostics.Add(new LevelDiagnostic("error", path, "Expected coordinate object"));
                return;
            }
            foreach (var key in new[] { "row", "col" })
                if (position[key] == null || !TryCoordinate(position[key]!, out _))
                    diagnostics.Add(new LevelDiagnostic("error", path + "." + key, "Expected finite numeric coordinate"));
        }

        private static void NumberDiagnostic(JToken? token, string path, double minimum, bool integer, List<LevelDiagnostic> diagnostics)
        {
            if (token == null || !TryCoordinate(token, out var number) || number < minimum || integer && !IsIntegralNumber(token))
                diagnostics.Add(new LevelDiagnostic("error", path, "Invalid numeric value"));
        }

        private static bool IsIntegralNumber(JToken token)
        {
            // Numeric tokens already passed the strict JSON grammar. Determine the
            // decimal scale from the preserved lexeme, without rounding via double.
            var text = token.ToString();
            var exponentAt = text.IndexOfAny(new[] { 'e', 'E' });
            var mantissa = exponentAt < 0 ? text : text.Substring(0, exponentAt);
            var dot = mantissa.IndexOf('.');
            var fraction = dot < 0 ? 0 : mantissa.Length - dot - 1;
            var trailingZeroes = 0;
            var nonzero = false;
            for (var i = mantissa.Length - 1; i >= 0; i--)
            {
                var character = mantissa[i];
                if (character == '.' || character == '-') continue;
                if (!nonzero && character == '0') trailingZeroes++;
                else nonzero = true;
            }
            if (!nonzero) return true;
            if (exponentAt < 0) return fraction <= trailingZeroes;
            var exponentText = text.Substring(exponentAt + 1);
            if (!long.TryParse(exponentText, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out var exponent))
                return exponentText[0] != '-';
            return exponent >= (long)fraction - trailingZeroes;
        }

        private static void EnumDiagnostic(JToken? token, string path, string[] names, string sentinel, List<LevelDiagnostic> diagnostics)
        {
            if (token != null)
            {
                if (IsAction(token, sentinel, names.Length))
                {
                    diagnostics.Add(new LevelDiagnostic("error", path, "Enum sentinel is not an executable value"));
                    return;
                }
                for (var i = 0; i < names.Length; i++) if (IsAction(token, names[i], i)) return;
            }
            diagnostics.Add(new LevelDiagnostic("warning", path, "Unknown enum value retained"));
        }
    }
}
