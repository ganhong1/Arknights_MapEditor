# 关卡音频流解码

输入为固定版本的 `audio-clip-object-receipt.json` 和 `audio-clip-manifest.json`。前者记录原包 SHA-256、资源容器、CAB 和 PathID，后者记录各来源事件到片段的关系。验证工具输出 FLAC 和逐片段证据，供离线试听和后续编辑器接入。

## 复现流程

使用 Python 3.10+、UnityPy 1.25.4、fmod_toolkit 0.1.3、FFmpeg 和已有的 Arkntools UnityFS 读取工具。在数据盘创建输出目录，并按以下顺序运行。

1. `python decode_audio_clips.py prepare <object-receipt.json> <原包目录> <work/sources.json>` 生成原包计划。
2. 用现有的 tsx 运行 `../art-resources/offline-pack/bundle_nodes.mjs prepare <work/sources.json> <work> <原包目录> <unity-js/src>`。它核对原包哈希，并解出标准 UnityFS 中间文件。
3. `python decode_audio_clips.py decode <object-receipt.json> <clip-manifest.json> <work> <decoded>` 从 CAB 和 PathID 定位对象，读取准确的流偏移和长度，解码为 PCM16 并导出 FLAC。输出目录必须是新目录。
4. `python decode_audio_clips.py index --manifest <clip-manifest.json> --receipt <decoded/decode-receipt.json> <index.json>` 生成事件索引。可重复传入 `--manifest` 和 `--receipt`，合并其他信号类别的验证数据。索引和各批次输出放在同一个父目录下。
5. `python -B -m pytest -q test_decode_audio_clips.py -p no:cacheprovider` 执行定向测试。成功导出后可以移除 `work/Staging` 中间目录，保留 `source-nodes.json`、片段和索引。

`decode-receipt.json` 记录流哈希、采样率、声道、帧数、PCM SHA-256 和 FLAC SHA-256。解码器校验帧数、采样率、声道与 AudioClip 元数据，并将 FLAC 全量回读到 PCM 与初次解码逐字节比对。静音属于有效输出，单独保留标志。事件至少要有一个来源版本的全部片段通过解码才记为完整。

## 2.7.81 结果

原始 469 项与波次换曲补录的 9 项共 478 项全部通过解码和 PCM 回读，涉及 257 个原包。源流全部为 FSB5，均为双声道，其中 448 项为 44.1 kHz、30 项为 48 kHz。FLAC 总大小 4,189,331,001 字节。一个约 0.452 秒的 `m_bat_normal03_intro` 解出全零 PCM，原始流和结果均保留。

| 事件 | 完整 | 当前基线不可用 |
| --- | ---: | ---: |
| 开局 BGM | 256 | 38 |
| 环境音 | 2 | 0 |
| `PLAY_BGM` | 17 | 7 |

开局 BGM 的 38 项中，37 项已有事件记录，1 项缺事件绑定。换曲的 7 项中，3 项有事件绑定但固定 PC 清单缺对应片段，4 项缺 `ON_CUSTOM_TRIGGER` 绑定。此次结果只包括原包解码与 FLAC 回读，Unity 编辑器内试听接入仍待完成。

## 历史资源补齐（2026-11-16）

用 [Torappu 资源索引](https://github.com/MooncellWiki/ak-asset-storage) 对照原版资源路径，新增 67 个来自 TOR 导出 WAV 的片段和 4 个历史 Android 原包片段。早期生息演算的 `collectarea`、`huntarea` 前奏和循环段来自官方批次 `23-02-22-22-37-04-ef2150`。四份 .dat 长度与 TOR 所列传输大小一致，解包后的 AB SHA-256 与索引一致。UnityFS v6 的目录块从头部结束处读起，现用同一工具处理这批旧包和原有 v8 包。

合并索引 `audio-event-decode-index-20261116.json` 共 549 个片段。开局音乐完整 293/294，环境音 2/2，波次换曲 20/24。未确认绑定的五个事件均有已解码的参考曲，在索引中单独列为 `previewCandidate`。其中 `bat_shatterpoint_02` 对应的参考曲 `shatterpoint_02` 在 PC 与 2.7.81 Android 原包的 PCM 一致。TOR 导出版本的 PCM 哈希与两份原包不同，因此保留原包解码作为该片段的选用来源。

通过 TOR 复核了引航者试炼第五期前后的音频表，`rglk1csghost`、`rglk1cswrtr`、`rl2boss2`、`rl2boss3` 仍只有开局音乐记录。原始 2.7.81 PC 解码记录保留。补源证据、事件表查询和逐片段收据在 `D:/Temporary_Embedded_Project/storage/map-editor-unity/audio-decode-20261115/followup-20261116`。

### 五个事件键的原包核对

直接读取本机 2.7.81 PC 的音频表和海蒂关卡 TextAsset。586 个 BGM bank 与 231 个别名中，`battle.ON_GAME_READY.shatterpoint_02` 有前奏和循环段。海蒂正文写入 `bat_shatterpoint_02`，完全一致的事件键及别名均缺失。同批次 TOR 的 10-3 等主线关卡使用 `shatterpoint_02`，这与曲目文件名中的 `m_bat_shatterpoint_02` 属于不同命名层。

另外读取活动期间的官方 Android 批次 `25-06-26-04-47-55_47709b`。TN-3 和 TN-4 正文包含四条换曲键，在同期 446 个 BGM bank 和 193 个别名里均只匹配 `ON_GAME_READY`。固定 2.7.81 PC 的调度把 `PLAY_BGM.key` 交给 `ON_CUSTOM_TRIGGER`，音频中间件以完整键查别名和 bank。该路径没有切换命名空间的回退。Boss Rush 预处理读原 wave 并统计启用的出怪动作，未发现改写这些音频键。对照样本 `shield` 和 `boss_sui` 通过原包显式别名复用开局音乐。

原包定位、TextAsset 哈希、解析结果与原生指令证据位于补源目录的 `native-lookup`。活动当期 Android 实际播放和服务器侧动态热更仍缺运行证据。

## Ogg Vorbis 试听库和 Unity 联动

Unity 2021.3 的 Audio Clip 文档将 Vorbis 用于音乐等长音频。本库采用 Ogg Vorbis，libvorbis 质量 5，保留采样率、声道与源样本长度。前奏和循环各保存一份。FLAC 作为验证母本留在原目录，未随 Unity 包复制。音频包排除环境音样本。

`python build_music_ogg.py <audio-event-decode-index-20261116.json> <新输出目录>` 读取已核验的 FLAC，要求 FFmpeg 和 FFprobe 在 PATH。每段检查来源 SHA-256、Vorbis 编码、采样率、声道、时长刻度和全量解码，成功后写入 manifest.json。547 段音乐全部通过，4,839,160,166 字节压缩为 722,650,323 字节。试听目录位于 `D:/Temporary_Embedded_Project/storage/map-editor-unity/music-editor-20261116/ogg-2.7.81`。可在选曲器选择其他副本的位置，或设置 `ARKNIGHTS_MAP_EDITOR_AUDIO_ROOT`。

`build_music_events.py` 由 2.7.81 原版音频表和验证索引生成插件的选曲数据。插件按 `ON_GAME_READY` 或 `ON_CUSTOM_TRIGGER` 查询事件与原版别名，声音分段从本地 Ogg 包按需加载。试听允许直接循环，也可以用片段或阶段中更早且已登记的换曲作为切换的前一首，缺少时使用本关开局曲。音响试听使用编辑模式预览播放器，与关卡调度时序分别验证。

- [Unity 2021.3 音频压缩](https://docs.unity3d.com/2021.3/Documentation/Manual/class-AudioClip.html)

### 音乐与音效的原版目录

先用 `build_effects_ogg.py <environment-audio-audit-20261116> <新输出目录>` 从已核验的 2.7.81 Bundle 和补源 WAV 生成环境及地图音效试听包。依赖 UnityPy、FFmpeg、FFprobe。脚本逐项比对 WAV 哈希、声道、采样率和采样数，回读全部 Ogg。插件通过资源库顶部的「试听包 → 音效试听包」设置路径，也支持环境变量 `ARKNIGHTS_MAP_EDITOR_EFFECTS_ROOT`。

运行 `build_audio_browser.py <2.7.81音频表JSON> <environment-preview-scope.json> <音乐试听包manifest.json> <音效试听包manifest.json> <audio-browser-2781.json>` 生成插件的原版目录。它会核对版本、原表 SHA-256、作用域事件和片段，并按音频包索引补入试听元数据。用途标签按已核验事件整理。两条自走棋羁绊音效在 2.7.81 音频表已登记，可试听，物件调用点是在后续客户端定位的。

### 资源库复核与试听包补录（2026-11-17）

2.7.81 原版音乐表的 807 条事件中，806 条引用了试听音轨，`sys.ON_ACTIVITY_LOADED.act17d7` 的前奏和循环字段均为空。941 段音轨已全部接入 Ogg 试听包，其中新补 394 段。319 段来自安装目录内的固定 PC 基础包，6 段来自固定 Android AssetBundle，43 段来自 TOR 索引定位的历史 Android AssetBundle，26 段来自 TOR 历史 WAV 导出。事件、别名、切换参数和资源键沿用原版音频表。

复核时用 AssetStudio Web 使用的 .idx 读取方式查资源归包，按原版资产路径匹配 AudioClip。历史包使用 TOR 的传输字节数与解包后 SHA-256 双重核验。原包音频经过 PCM、FLAC 回读和 Ogg 解码检查，TOR WAV 按完整相对路径和修改时间核对。音效沿用已确认的 227 条关卡音效事件和 144 段试听文件。

音乐试听包仍位于 `D:/Temporary_Embedded_Project/storage/map-editor-unity/music-editor-20261116/ogg-2.7.81`。本轮逐音轨来源、兼容性对照和验证记录位于 `D:/Temporary_Embedded_Project/storage/map-editor-unity/library-refresh-20261117`。
