# UGUI 页面预制体

当前可直接在 Unity 的 Prefab Mode 中修改：

- `Assets/Resources/BombTavern/Prefabs/UI/HomeMenu.prefab`：主页标题、按钮、图标与左侧暗部。游戏启动时由 `PreGameMenuUgui` 加载。
- `Assets/Resources/BombTavern/Prefabs/UI/RulesTutorial.prefab`：游戏说明弹窗、滚动区和关闭按钮。首次进入、主页“游戏说明”和设置中的规则入口都加载这一个预制体。
- `Assets/Resources/BombTavern/Prefabs/UI/SettingsPopup.prefab`：主页和对局内共用的游戏设置弹窗；可调整顶部标题、左侧页签、各设置行、滑条和底部三个按钮。对局内的返回大厅入口位于 `AssistPage`。
- `Assets/Resources/BombTavern/Prefabs/UI/StartGameSetup.prefab`：点击主页“开始游戏”后的二级界面；左侧四张角色卡、右侧昵称与入局方式、返回按钮和底部流程条都可在 Prefab Mode 调整。素材为 `StartGameSetupSheet.png`。
- `Assets/Resources/BombTavern/Prefabs/UI/StatisticsPopup.prefab`：战绩统计弹窗。四项汇总、近期十局、详情、环形图、模式比例条和刷新按钮均是可编辑 UGUI 节点；图集 `StatisticsSheet.png` 使用正确的“战绩统计”标题。
- `Assets/Resources/BombTavern/Prefabs/UI/GameSetupRules.prefab`：开始前的地图、炸弹、概率偏移、限时设置。根节点铺满 Canvas，`GameSetupBlocker` 负责拦住底层点击，`GameSetupPanel` 为中央面板。
- `Assets/Resources/BombTavern/Prefabs/UI/GameOver.prefab`：对局结束排行、各列统计、返回大厅、再来一局和详细战绩。
- `Assets/Resources/BombTavern/Prefabs/UI/LobbyPanel.prefab`：等待连接/入座面板，包括昵称、地址输入、连接状态、四个座位和开始游戏按钮。

调整 `RectTransform`、图片、文字和字号后保存预制体即可，运行时会使用保存后的版本。素材位于 `Assets/Resources/BombTavern/UI`；新版设置弹窗专用图集是 `SettingsSheetV2.png`，不再使用旧版设置图集。切片可在 Sprite Editor 调整。中文文字使用项目内的 `NotoSansSC-Dynamic.asset`。

为了保持功能绑定，请保留主页五个按钮的物体名：`Home_开始游戏`、`Home_游戏说明`、`Home_设置`、`Home_战绩统计`、`Home_退出游戏`。游戏说明中的 `RulesTutorialBlocker`、`RulesTutorialPanel`、`RulesScrollViewport` 和 `Close` 也需要保留；其余视觉子节点可自由改名。增加或删除规则内容时，同时调整 `RulesContent` 的高度，滚动区域才会完整覆盖内容。

编辑器菜单“工具 → 炸弹酒馆 → 9. 创建主页与游戏说明预制体”只补建缺失的预制体，不覆盖已经保存的手工排版。后续新增页面沿用“可编辑预制体 + 运行时绑定行为”的方式。

设置预制体也遵循只补建规则：菜单“工具 → 炸弹酒馆 → 10. 创建设置预制体”。为保持运行时绑定，请保留设置弹窗中 `Panel`、`Shade`、`Close`、`TabDisplay`、`TabAudio`、`TabAssist`、`DisplayPage`、`AudioPage`、`AssistPage`、各设置控件和底部 `Restore`、`LeaveSettings`、`Confirm` 的物体名。可以更改它们的 RectTransform 和视觉子节点。底部只有“恢复默认 / 退出设置 / 确定”三个按钮；设置即时生效，确定和退出设置都会保存。对局中打开此弹窗不会暂停联机游戏。

开始游戏二级界面的角色卡必须保留 `Skin_<皮肤 ID>` 名称，卡片中可替换 `Portrait` 图片（当前为项目内四个角色预制体渲染的透明立绘，位于 `UI/Portraits`）；选中与未选中状态分别使用图集中的 `SetupCardSelected` 和 `SetupCardNormal`，包括第一张卡切走后也会恢复普通边框。右侧控件 `PlayerNameInput`、`Mode_0` 至 `Mode_2`、`AddressInput`、`PortInput`、`ScanButton`、`StartButton` 由运行时绑定原有游戏流程。编辑器菜单“工具 → 炸弹酒馆 → 13. 创建开始游戏二级界面预制体”只在文件缺失时创建，不覆盖手工排版。

`Resolution`、`WindowMode`、`Quality` 是展开式下拉框，内部 `Options` 与 `OptionTemplate` 用于动态生成选项；调整排版时请保留这两个子节点及 `DropdownDismiss`。分辨率、窗口模式、画质、垂直同步、帧率上限、亮度、UI 缩放，以及四项音量均与运行时设置绑定。改完预制体后无需重新运行创建菜单，否则会覆盖手工排版。

2026-10-09 后台验收已修正二级页面缩放、联机输入行和三页设置内容的底栏间距。普通按钮的直线切角边框为 `SettingsButtonDark.png` / `SettingsButtonGold.png`；真正下拉框仍使用新版图集的箭头素材。设置页与教程淡出期间保持遮罩，完全隐藏后释放游戏输入。修改各页时请给 `Footer` 留出空间，并保持滑条轨道、手柄的 `Raycast Target` 开启。

`UiAcceptanceRepair` 只供显式执行本轮修复，会重新调整部分预制体排版；正常启动和打开项目不会调用。已经手工调整后不要再次运行它。后台复跑方法及未测范围见 `Docs/UI后台验收报告_2026-10-09.md`。

新增的对局规则、结算和等待连接预制体使用 `UiPrefabBindings` 保存对视图字段的引用。可调整排版、字号、图片和按钮显示文字；请保留 `Btn_返回`、`Btn_确认并开始`、`Btn_返回大厅`、`Btn_详细战绩`、`Btn_确认昵称`、`Btn_退出到主界面` 等功能按钮名称，并保留绑定组件中的引用。不要删除被引用的控件。等待连接和结算界面包含 Layout Group；调整间距、Padding 和子控件的 Layout Element，避免只拖动被布局组接管的坐标。

战绩弹窗请保留 `StatisticsPanel`、`Shade`、`Close`、`Refresh`、`Recent_0` 至 `Recent_9`、`Details`、`DetailsClose`、`DetailText` 及数据文字节点名。`StatisticsPopupView` 的 `Design Size` 和 `Preferred Scale` 控制适配范围；`WinRing` 上的 `StatisticsRingGraphic` 可调整环宽、颜色，模式条是 Filled Image，由实际统计设置 Fill Amount。演示的数值会在打开弹窗时被本机真实记录替换，空历史显示空状态。

编辑器菜单“16. 创建战绩统计预制体”和“17. 补齐既有弹窗预制体”只补建缺失文件。`StatisticsPrefabBuilder.RepairAndBuild` 是本次显式验收修复入口，会重设部分新页面的排版和底板；手工微调后不要运行此入口。`UiButtonMotion` 已移到独立同名脚本，避免按钮动效组件保存成缺失脚本。

2026-10-09 设置重新拼装：保留用户在 Sprite Editor 保存的 `SettingsSheetV2.png.meta`，不重新自动切片。`SettingsSheetV2_0` 是独立标题，`SettingsSheetV2_1` 是独立主框；设置预制体内分别为 `Panel/TitleArt` 和 `Panel/WindowFrame`。主框的九宫格派生资源为 `UI/UserSlices/SettingsWindowFrame.asset`，保留原图集裁切范围，原切片名称、pivot、border 和 GUID 不变。设置底板为 `Panel/WindowBacking`，可直接调整颜色和排版。

下拉框和滑条沿用用户切片的 Border。下拉控件使用 `Pixels Per Unit Multiplier = 2` 避免上下边压扁。Slider 会驱动手柄 Y 锚点铺满父区域，最终高度是父区域高度加 `Size Delta.y`；当前手柄差值设为 1，避免再次变成长椭圆。修改时观察运行后的手柄大小。

对局规则、结算、等待连接页使用深棕底板和已有战绩图集金框，`WindowBacking` 是不接收点击、忽略布局组的装饰框；父面板负责实底和输入阻挡，其他控件保留原有引用。主页、游戏说明、开始页和统计页已有统一的金色/棕色风格，本轮保留其现有排版。

设置的显式重建菜单检测到上述用户切片后，也会沿用保存的切片。重建仍会恢复本轮设置布局；手工微调后正常保存即可，不要重复执行重建。四个更新前预制体和原图集 meta 已备份在 `Build/UIStyleBackup_2026-10-09`。

若图集已有切片而独立标题/主框名称不匹配，创建或重建菜单会报错并保留现有记录，不会退回旧的自动切片。结算页布局组新增左右及底部留白，使底部说明不与金框重叠；微调后也请保留这段间距。
