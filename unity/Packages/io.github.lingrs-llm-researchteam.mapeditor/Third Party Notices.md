# Third-party notices

This package depends on Unity's `com.unity.nuget.newtonsoft-json` 3.2.2 package.
Its Newtonsoft.Json distribution and notices remain supplied by that dependency;
no duplicate Newtonsoft binary is bundled here. Test development uses Unity Test
Framework and NUnit supplied by the development project's packages.

The repository code license is in LICENSE.md. The package also includes
game-derived LevelData samples, portraits, deployment artwork and editor atlases
with PRTSMap-derived presentation data. See the catalog's
[SOURCES.md](Editor/Catalog/SOURCES.md) and the
[gallery inventory](Samples~/CompletePrtsGallery/README.md) for their provenance.
Game assemblies, full AssetBundles and Unity Editor binaries are outside this package.

The catalog search dependencies carry separate notices for
[Fuse.js](Editor/Catalog/SearchDependencies/LICENSE-Fuse.txt),
[Jint](Editor/Catalog/SearchDependencies/LICENSE-Jint.txt),
[Esprima](Editor/Catalog/SearchDependencies/LICENSE-Esprima.txt) and
[pinyin-pro](Editor/Catalog/SearchDependencies/LICENSE-pinyin-pro.txt).

## Noto Sans SC

The editor workbench includes the upstream Simplified Chinese regular OTF from [Noto Sans CJK 2.004](https://github.com/notofonts/noto-cjk/releases/tag/Sans2.004), available under the [SIL Open Font License 1.1](Editor/UI/NotoSansSC-OFL.txt). The font is bundled without modification. Copyright © 2014–2021 Adobe. The SHA-256 is `faa6c9df652116dde789d351359f3d7e5d2285a2b2a1f04a2d7244df706d5ea9`.

The Unity 2021.3 TextCore asset uses prebuilt static atlases generated from the workbench and catalog text. The original OTF remains available with the package.
