import unittest

from plan_pack import candidate_kind, closure, sort_paths


class PackPlanTests(unittest.TestCase):
    def test_scene_catalog_ignores_dependencies_as_separately_selectable_scenes(self):
        self.assertEqual("scene", candidate_kind({
            "key": "scenes/obt/main/map/map",
            "unityPath": "dyn/scenes/obt/main/map/map.unity",
        }))
        self.assertIsNone(candidate_kind({
            "key": "scenes/obt/main/map/lighting",
            "unityPath": "dyn/scenes/obt/main/map/lighting.asset",
        }))

    def test_effect_and_camera_candidates_keep_native_namespaces(self):
        cases = (
            ("battle/prefabs/effects/map_038_sign_glow_02", "effect"),
            ("battle/prefabs/effects/absin_attack_01", None),
            ("ui/rogueliketopic/topics/rogue_4/cameraplugin/rogue_4_boss4", "camera"),
            ("ui/levelrelatedcameraplugin/common_camera_move_plugin", "camera"),
            ("ui/camerapluginish/example", None),
            ("arts/maps/map_lm_center/background_mesh/bg_lmc", "map-art"),
        )
        for key, expected in cases:
            with self.subTest(key=key):
                self.assertEqual(expected, candidate_kind({"key": key, "unityPath": key + ".prefab"}))

    def test_bundle_closure_deduplicates_transitive_dependencies_and_cycles(self):
        graph = {"scene": ["shared", "shader"], "prefab": ["shared"],
                 "shared": ["shader"], "shader": ["shared"], "unrelated": []}
        self.assertEqual({"scene", "prefab", "shared", "shader"}, closure(("scene", "prefab"), graph))

    def test_bundle_paths_have_stable_secondary_order(self):
        self.assertEqual(["a", "X", "x", "y"], sort_paths({"x", "X", "a", "y"}))


if __name__ == "__main__":
    unittest.main()
