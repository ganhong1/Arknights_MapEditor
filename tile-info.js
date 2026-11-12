(function(g){const data={
  "tile_character": {
    "name": "字格",
    "description": "间隔一定时间会被拓印，若其上有字，根据字产生效果",
    "occurrence": "TA-1 激石波; levels/activities/act49side/level_act49side_01.json",
    "conditions": "heightType=LOWLAND, buildableType=MELEE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_character; WordTile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_dygmny_1": {
    "name": "化境地块",
    "description": "会有更高的概率出现各种奇妙的玩意",
    "occurrence": "模式/活动关卡，名称未关联; levels/obt/roguelike/ro5/dlc1/level_rogue5_1-1_dlc1.json",
    "conditions": "heightType=LOWLAND, buildableType=MELEE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_dygmny_1; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_dygmny_3": {
    "name": "化境地块",
    "description": "会有更高的概率出现各种奇妙的玩意",
    "occurrence": "模式/活动关卡，名称未关联; levels/obt/roguelike/ro5/dlc1/level_rogue5_1-1_dlc1.json",
    "conditions": "heightType=HIGHLAND, buildableType=RANGED, passableMask=FLY_ONLY; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_dygmny_3; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_end": {
    "name": "保护目标",
    "description": "蓝色目标点，敌方进入后会减少此目标点的耐久",
    "occurrence": "GT-1 日正当中; levels/activities/a001/level_a001_01.json",
    "conditions": "heightType=LOWLAND, buildableType=NONE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_end; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_fence_bound": {
    "name": "围墙",
    "description": "可放置近战单位，敌方无法进入",
    "occurrence": "DC-3 水涨池深; levels/activities/act14mini/level_act14mini_03.json",
    "conditions": "heightType=LOWLAND, buildableType=MELEE, passableMask=FLY_ONLY; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_fence_bound; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_floor": {
    "name": "不可放置位",
    "description": "不可放置单位，可以通行",
    "occurrence": "GT-1 日正当中; levels/activities/a001/level_a001_01.json",
    "conditions": "heightType=LOWLAND, buildableType=NONE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_floor; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_flystart": {
    "name": "空袭侵入点",
    "description": "敌方飞行单位会从此处进入战场",
    "occurrence": "GT-HX-2 关山飞渡; levels/activities/a001/level_a001_ex05.json",
    "conditions": "heightType=HIGHLAND, buildableType=NONE, passableMask=FLY_ONLY; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_flystart; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_forbidden": {
    "name": "禁入区",
    "description": "不可放置单位，不可通行",
    "occurrence": "GT-1 日正当中; levels/activities/a001/level_a001_01.json",
    "conditions": "heightType=HIGHLAND, buildableType=NONE, passableMask=FLY_ONLY; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_forbidden; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_grass": {
    "name": "草丛",
    "description": "置于其中的干员不会成为敌军远程攻击的目标",
    "occurrence": "GT-EX-3 与狼共舞; levels/activities/a001/level_a001_ex03.json",
    "conditions": "heightType=HIGHLAND, buildableType=RANGED, passableMask=FLY_ONLY; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_grass; BuffTile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_hole": {
    "name": "地穴",
    "description": "危险的凹陷地形或地面破洞，经过的敌人会摔落至底部直接死亡",
    "occurrence": "GT-3 意外之旅; levels/activities/a001/level_a001_03.json",
    "conditions": "heightType=LOWLAND, buildableType=NONE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_hole; HoleTile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_pollution_roadf": {
    "name": "说明表未列名",
    "description": "无说明文本；不能仅凭ID推断效果",
    "occurrence": "H16-2 异常预案-2; levels/obt/hard/level_hard_16-02.json",
    "conditions": "heightType=LOWLAND, buildableType=NONE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_pollution_roadf; DynamicBuffTileFixed",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_road": {
    "name": "平地",
    "description": "可以放置近战单位，可以通行",
    "occurrence": "GT-1 日正当中; levels/activities/a001/level_a001_01.json",
    "conditions": "heightType=LOWLAND, buildableType=MELEE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_road; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_sleep_road": {
    "name": "入梦砖",
    "description": "置于其中的干员会立刻被拖入美梦中",
    "occurrence": "IG-S-3 压轴大戏; levels/activities/act2multi/level_act2multi_hard-03.json",
    "conditions": "heightType=LOWLAND, buildableType=MELEE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_sleep_road; BuffTile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_sleep_wall": {
    "name": "入梦砖",
    "description": "置于其中的干员会立刻被拖入美梦中",
    "occurrence": "IG-S-3 压轴大戏; levels/activities/act2multi/level_act2multi_hard-03.json",
    "conditions": "heightType=HIGHLAND, buildableType=RANGED, passableMask=FLY_ONLY; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_sleep_wall; BuffTile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_start": {
    "name": "侵入点",
    "description": "敌方会从此进入战场",
    "occurrence": "GT-1 日正当中; levels/activities/a001/level_a001_01.json",
    "conditions": "heightType=LOWLAND, buildableType=NONE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_start; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_telin": {
    "name": "通道入口",
    "description": "敌方会从此进入通道，从通道出口出现",
    "occurrence": "TB-3 沸腾俱乐部; levels/activities/act10mini/level_act10mini_03.json",
    "conditions": "heightType=LOWLAND, buildableType=NONE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_telin; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_telout": {
    "name": "通道出口",
    "description": "进入通道的敌方单位会从此处再度出现",
    "occurrence": "TB-3 沸腾俱乐部; levels/activities/act10mini/level_act10mini_03.json",
    "conditions": "heightType=LOWLAND, buildableType=NONE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_telout; Tile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_toxic": {
    "name": "源石污染区",
    "description": "使位于其上的单位持续受到真实伤害并且技力回复速度大幅度降低",
    "occurrence": "SE-1 灼热的风; levels/activities/act18mini/level_act18mini_01.json",
    "conditions": "heightType=LOWLAND, buildableType=NONE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_toxic; DynamicBuffTileFixed",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_toxichill": {
    "name": "源石污染区",
    "description": "使位于其上的单位持续受到真实伤害并且技力回复速度大幅度降低",
    "occurrence": "SE-1 灼热的风; levels/activities/act18mini/level_act18mini_01.json",
    "conditions": "heightType=HIGHLAND, buildableType=NONE, passableMask=FLY_ONLY; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_toxichill; DynamicBuffTileFixed",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_toxicroad": {
    "name": "源石污染区",
    "description": "使位于其上的单位持续受到真实伤害并且技力回复速度大幅度降低",
    "occurrence": "SE-1 灼热的风; levels/activities/act18mini/level_act18mini_01.json",
    "conditions": "heightType=LOWLAND, buildableType=MELEE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_toxicroad; DynamicBuffTileFixed",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_toxicwall": {
    "name": "源石污染区",
    "description": "使位于其上的单位持续受到真实伤害并且技力回复速度大幅度降低",
    "occurrence": "SE-1 灼热的风; levels/activities/act18mini/level_act18mini_01.json",
    "conditions": "heightType=HIGHLAND, buildableType=RANGED, passableMask=FLY_ONLY; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_toxicwall; DynamicBuffTileFixed",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_volcano": {
    "name": "热泵通道",
    "description": "每隔一段时间便会喷出高温气体，对其上的任何单位造成无视防御和法抗的伤害",
    "occurrence": "SV-3 浪潮; levels/activities/act10d5/level_act10d5_03.json",
    "conditions": "heightType=LOWLAND, buildableType=MELEE, passableMask=ALL; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_volcano; CastTile",
    "verification": "构造/回读/新局暂停通过"
  },
  "tile_wall": {
    "name": "高台",
    "description": "可以放置远程单位，不可通行",
    "occurrence": "GT-1 日正当中; levels/activities/a001/level_a001_01.json",
    "conditions": "heightType=HIGHLAND, buildableType=RANGED, passableMask=FLY_ONLY; blackboard/effects保留原样",
    "resources": "battle/prefabs/[uc]tiles/tile_wall; Tile",
    "verification": "构造/回读/新局暂停通过"
  }
};if(typeof module!=="undefined")module.exports=data;else g.TileInfo=data;})(globalThis);
