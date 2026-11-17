# Options.configBlackBoard 逐键约束调查

范围：固定PC 2.7.81原生分支及3899份本地LevelData样例；不是全版本合法键总表。未改插件。详见 OPTIONS_VALUE_RESEARCH_20261106.md 的来源、类型转换及错误修正。

状态说明：consumer-partial表示找到相关消费者和读取/部分解析规则，**不表示全部业务边界闭合**；sample-only表示仍只有样例。rect系列存在同名跨模式，不能据Act42Side消费者推定Sandbox行为。

| 键 | 读取形式 | 缺失回退 | 约束及仍待确认 | 证据VA | 样例（非默认/非枚举） |
|---|---|---|---|---|---|
| dialog_before_battle | 字符串 | 空串 | 对白信号/路径；需对应模式插件和资源。不是任意文本即可生效。 | 0x180C90C10 / 0x180C90AA0 | value=0.0, valueStr="Obt/Tutorial/Level/main_14-10_a" |
| dialog_after_battle | 字符串 | 空串 | 对白信号/路径；需对应模式插件和资源。不是任意文本即可生效。 | 0x180C90C10 / 0x180C90AA0 | value=0.0, valueStr="Obt/Tutorial/Level/main_14-10_b" |
| rect_1 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_2 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_3 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_4 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_5 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_6 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_7 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_8 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_9 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_10 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_11 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_12 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_13 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_14 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_15 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_16 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_17 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_18 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_19 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| rect_20 | 矩形列表字符串 | 空值不创建该区域 | 已证Act42Side扫描Contains(rect_)，非空才拆分ParseRect；并非固定20项枚举。Sandbox同名键的具体消费者仍需独立核验，不能混用模式。 | 0x18078C3A0 / 0x186727E20 | value=0.0, valueStr="" |
| enable_war_fog | 数值布尔 | false | 建议写0/1；按近零比较读布尔。仅对应Sandbox/Racing等消费者激活时生效。 | 0x180E8B7C0 / 0x180D37B70 | value=1.0, valueStr=null |
| enable_camera | 数值布尔 | false | 建议写0/1；按近零比较读布尔。仅对应Sandbox/Racing等消费者激活时生效。 | 0x180E8B7C0 / 0x180D37B70 | value=1.0, valueStr=null |
| disable_character_gbuff | 数值布尔 | false | 建议写0/1；按近零比较读布尔。仅对应Sandbox/Racing等消费者激活时生效。 | 0x180E8B7C0 / 0x180D37B70 | value=0.0, valueStr=null |
| disable_enemy_gbuff | 数值布尔 | false | 建议写0/1；按近零比较读布尔。仅对应Sandbox/Racing等消费者激活时生效。 | 0x180E8B7C0 / 0x180D37B70 | value=0.0, valueStr=null |
| CAMERA_VIEW_LEVEL | 整数枚举 | 0 | DEFAULT=0/MAX=1/MEDIUM=2/ULTIMATE=3/UP=4；直接索引CAMERA_VIEWS，负数或越界不可用。 | 0x1806B3960 / 0x1806B4510 | value=3.0, valueStr=null |
| CAMERA_START_GRID | 坐标字符串 | 缺失/解析失败不改起点 | TryParseVector2Position；地图尺寸参与从格坐标到相机坐标变换。语法完整边界待核。 | 0x1806B3960 | value=0.0, valueStr="(6,6)" |
| construct_view_scale | Single | 1 | 传给Focus；此入口没有正数钳制。样例含0，不能直接宣布0非法。相机后续边界待核。 | 0x180DA69F0 | value=0.0, valueStr=null |
| construct_view_center_position | 格坐标字符串 | 地图中心回退 | 解析后GetTile；找不到目标格回退中心。建议做坐标引用核验。 | 0x180DA5C40 | value=0.0, valueStr="14,24" |
| timeline_view_path | 资源字符串 | sandbox_v3_camera_default | 拼接AVG/Timeline/Playable/类资源前缀后加载PlayableAsset；具体路径以原始字节/资源核验，不能只看非空。 | 0x180DA6370 | value=0.0, valueStr="sandbox_v3_camera_01" |
| quest_check | 任务ID字符串 | 无任务检查ID | TryGetString；需任务上下文，缺失返回false。合法ID全集不是本键的数值枚举。 | 0x180DA98E0 | value=0.0, valueStr="mainline1_3a" |
| legion_init_random_shuffle | 数值布尔 | 已有m_input值 | GetBoolOrDefault，不应统一补false；仅Legion预处理生效。 | 0x180C481B0 | value=1.0, valueStr=null |
| legion_recycle_random_shuffle | 数值布尔 | 已有m_input值 | GetBoolOrDefault，不应统一补false；仅Legion预处理生效。 | 0x180C481B0 | value=1.0, valueStr=null |
| legion_init_card_count | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=10.0, valueStr=null |
| legion_init_redraw_count | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=4.0, valueStr=null |
| legion_ingame_redraw_count | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=3.0, valueStr=null |
| legion_gold_for_endprepare | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=0.0, valueStr=null |
| legion_gold_for_wave_end | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=40.0, valueStr=null |
| legion_init_gold | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=30.0, valueStr=null |
| legion_init_card_price | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=10.0, valueStr=null |
| legion_card_price_growth | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=0.0, valueStr=null |
| legion_max_card_price | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=10.0, valueStr=null |
| add_price_when_reshuffle | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=2.0, valueStr=null |
| max_add_price_when_reshuffle | 整数 | 已有m_input值 | 入口GetIntOrDefault并赋值，未证明统一上下限；数量、价格及增长需下游联动。 | 0x180C481B0 | value=10.0, valueStr=null |
| branch_interval | Single | 0 | TryGetFloat后保存生成间隔；样例含0。负数和后续触发节流边界仍待核。 | 0x180BD2440 / 0x180BD4AE0 | value=5.0, valueStr=null |
| default_pos_x | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=6.0, valueStr=null |
| default_pos_y | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=4.7, valueStr=null |
| default_size | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=0.93, valueStr=null |
| enemy_pos_x | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=25.0, valueStr=null |
| enemy_pos_y | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=4.7, valueStr=null |
| enemy_size | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=0.93, valueStr=null |
| enemy_place_rect | 矩形字符串 | 未证明可省略 | ParseRect后参与敌方预览格枚举及mapData访问，不能无界；范围需与地图、可用格一致。 | 0x180B613D0 | value=0.0, valueStr="((14,7),(18,13))" |
| battle_pos_x | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=8.0, valueStr=null |
| battle_pos_y | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=5.68, valueStr=null |
| battle_size | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=0.82, valueStr=null |
| move_time | Single | 0 | AutoChess相机移动时长；此读取入口不钳制，负值及缓动后续待核。 | 0x180BA7830 | value=0.25, valueStr=null |
| coin_panel_pos | 仅样例类型已知 | 未知 | 固定原生中尚未定位消费者；疑似旧AutoChess参数但不能断言废弃。保留原值，不提供伪造边界。 | 未定位 | value=0.0, valueStr="(7,0)" |
| left_prepare_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(-4.67,0.3,-2.46,1)" |
| left_shop_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(-4.83,-1.75,-3.77,1)" |
| left_battle_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(-4,1.91,-0.6,1)" |
| right_battle_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(4,1.91,-0.6,1)" |
| mid_battle_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(0,0,-4.22,1)" |
| left_boss_prepare_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(-4.67,-6.68,-2.46,1)" |
| left_boss_shop_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(-4.83,-8.73,-3.77,1)" |
| left_boss_battle_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(-4,-5.54,-0.6,1)" |
| right_boss_battle_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(4,-5.54,-0.6,1)" |
| mid_boss_battle_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(0,-7.95,-4.22,1)" |
| right_boss_prepare_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(5.93,-6.68,-2.46,1)" |
| right_boss_shop_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(5.83,-8.73,-3.77,1)" |
| leftNormal | 矩形列表字符串 | 缺失记错误 | 按分隔符拆分再ParseRect；矩形两端闭区间枚举整数格，零Rect跳过。不是统一x/y/宽/高四值输入。 | 0x180B68260 | value=0.0, valueStr="((6,0),(12,10))" |
| rightNormal | 矩形列表字符串 | 缺失记错误 | 按分隔符拆分再ParseRect；矩形两端闭区间枚举整数格，零Rect跳过。不是统一x/y/宽/高四值输入。 | 0x180B68260 | value=0.0, valueStr="((6,11),(12,20))" |
| leftBoss | 矩形列表字符串 | 缺失记错误 | 按分隔符拆分再ParseRect；矩形两端闭区间枚举整数格，零Rect跳过。不是统一x/y/宽/高四值输入。 | 0x180B68260 | value=0.0, valueStr="((0,0),(6,10))" |
| rightBoss | 矩形列表字符串 | 缺失记错误 | 按分隔符拆分再ParseRect；矩形两端闭区间枚举整数格，零Rect跳过。不是统一x/y/宽/高四值输入。 | 0x180B68260 | value=0.0, valueStr="((0,11),(6,20))" |
| enemy_camera_param | 四数参数字符串 | 未证明可省略 | AutoChess正则匹配四个捕获组，ToSingle后赋position.xyz和size；格式错误记日志。完整正则/非法浮点异常仍待核。 | 0x180BA7830 / 0x180BA86D0 | value=0.0, valueStr="(0,7.86,0.9,1)" |
| player_map_lr_boundary_col | 整数 | 管理器已有字段值 | GetIntOrDefault；边界列和偏移与地图/区域共同约束，未证明固定数值白名单。 | 0x180B67FD0 | value=10.0, valueStr=null |
| player_map_lr_offset | 整数 | 管理器已有字段值 | GetIntOrDefault；边界列和偏移与地图/区域共同约束，未证明固定数值白名单。 | 0x180B67FD0 | value=8.0, valueStr=null |
| player_map_ud_offset | 整数 | 管理器已有字段值 | GetIntOrDefault；边界列和偏移与地图/区域共同约束，未证明固定数值白名单。 | 0x180B67FD0 | value=7.0, valueStr=null |
| vase_start_character_count | 整数 | 1 | 取得选人数量；还需满足可用角色/花瓶上下文，未证明全局上限。 | 0x180C8AB30 | value=1.0, valueStr=null |
| vase_character_vase_count | 整数 | 0 | 取得角色花瓶数量；需对应可分配格与角色资源，未证明全局上限。 | 0x180C8AA80 | value=6.0, valueStr=null |
| fallback_trap | 卡牌别名字符串 | 默认字面量未解码 | Deck.FindCardByAlias查找；找不到会记录错误。例trap_031_sleep#1是别名引用，不等同装置ID。 | 0x180763870 | value=0.0, valueStr="trap_031_sleep#1" |
| roll_when_select | 数值布尔 | false | TryGetBool成功且结果true才启用；需花瓶选择流程。 | 0x180768260 | value=1.0, valueStr=null |
| stadium_type | 整数枚举 | 0 | 0 RANDOM/1 NORMAL/2 ICE/3 THUNDER/4 CATCH_BALL/5 WHIRL_WIND；0触发权重表随机。 | 0x180C21CA0 | value=1.0, valueStr=null |
| win_goal_difference | 整数 | 5 | 样例含-1；入口未钳制，最终胜负判定仍需继续追踪，不能禁负数。 | 0x180C21CA0 | value=5.0, valueStr=null |
| team_type | 整数/表类型引用 | 0 | 负值跳过随机队伍选择；0使用全部权重；正值筛选表中同type记录，必须有候选。不是简单正整数。 | 0x1809493F0 | value=-1.0, valueStr=null |
| delta_buildable_cnt_penalty | 整数差值 | 0 | 样例为-4；负数是已有用法，不能按数量字段统一校验>=0。 | 0x180C21CA0 | value=-4.0, valueStr=null |
| boss_branch_trigger_time | Single→FP | GetFloat缺键行为需结合getter | HalfIdle保存触发时间；与分支和模式时间相关，入口无统一上限。 | 0x180C37AB0 | value=300.0, valueStr=null |
| enemy_wave_appear_time | 数值或字符串列表 | 字符串回退空串 | 先TryGetFP：数值形成单项；否则字符串拆分，各项ToInt32再FP，最后Sort。样例140&#124;190；两种格式不可强制相互覆盖。 | 0x180C37AB0 | value=0.0, valueStr="140&#124;190" |
| max_enemy_capacity | 整数 | GetInt缺键行为需结合getter | HalfIdle另乘enemyOverloadWarningRatio计算预警数量；需下游容量机制，不是任意Int32安全。 | 0x180C37AB0 | value=20.0, valueStr=null |

## 样例之外发现的原生键

- Legion：legion_g_buff_dying_duration，由GetIntOrDefault读取，缺失保留输入值。
- Sandbox：disable_trap_gbuff、enable_normal_enemy_when_rush，由GetBoolOrDefault(false)读取。
- 这些键再次说明89只是本地语料观测数，并不是完整合法键集合。

## 适合表单的确定部分

- Boolean键推荐开关写0/1，保留原始非标准数值并提示；整数键说明Floor语义。
- 相机档位、球场类型可做已知枚举选项；未知版本值应保留并标记，不能静默替换。
- 别名/任务/资源路径采用引用选择与存在性校验；不可替换为全局字符串白名单。
- 四数相机参数、坐标、矩形与时间列表可做结构化助手，但完整解析语法闭合前保留原始文本。
- 数量/价格/时间没有证据的上下限只提示，不伪造硬校验。
- 大小写同名、数值/字符串双重记录、空串和缺失的差异必须保留。

2026-11-09 展示修正：样例列分别展示value与valueStr，避免将空字符串的首样例误显示为数值0；不改变原始语料和已证消费者规则。
