# UGUI 页面预制体

当前可直接在 Unity 的 Prefab Mode 中修改：

- `Assets/Resources/BombTavern/Prefabs/UI/HomeMenu.prefab`：主页标题、按钮、图标与左侧暗部。游戏启动时由 `PreGameMenuUgui` 加载。
- `Assets/Resources/BombTavern/Prefabs/UI/RulesTutorial.prefab`：游戏说明弹窗、滚动区和关闭按钮。首次进入、主页“游戏说明”和设置中的规则入口都加载这一个预制体。
- `Assets/Resources/BombTavern/Prefabs/UI/SettingsPopup.prefab`：主页和对局内共用的游戏设置弹窗；可调整顶部标题、左侧页签、各设置行、滑条和底部三个按钮。对局内的返回大厅入口位于 `AssistPage`。

调整 `RectTransform`、图片、文字和字号后保存预制体即可，运行时会使用保存后的版本。素材来自 `Assets/Resources/BombTavern/UI` 中的三张图集；切片可在 Sprite Editor 调整。中文文字使用项目内的 `NotoSansSC-Dynamic.asset`。

为了保持功能绑定，请保留主页五个按钮的物体名：`Home_开始游戏`、`Home_游戏说明`、`Home_设置`、`Home_战绩统计`、`Home_退出游戏`。游戏说明中的 `RulesTutorialBlocker`、`RulesTutorialPanel`、`RulesScrollViewport` 和 `Close` 也需要保留；其余视觉子节点可自由改名。增加或删除规则内容时，同时调整 `RulesContent` 的高度，滚动区域才会完整覆盖内容。

编辑器菜单“工具 → 炸弹酒馆 → 9. 创建主页与游戏说明预制体”只补建缺失的预制体，不覆盖已经保存的手工排版。后续新增页面沿用“可编辑预制体 + 运行时绑定行为”的方式。

设置预制体也遵循只补建规则：菜单“工具 → 炸弹酒馆 → 10. 创建设置预制体”。为保持运行时绑定，请保留设置弹窗中 `Panel`、`Shade`、`Close`、`TabDisplay`、`TabAudio`、`TabAssist`、`DisplayPage`、`AudioPage`、`AssistPage`、各设置控件和底部 `Restore`、`LeaveSettings`、`Confirm` 的物体名。可以更改它们的 RectTransform 和视觉子节点。底部只有“恢复默认 / 退出设置 / 确定”三个按钮；设置即时生效，确定和退出设置都会保存。对局中打开此弹窗不会暂停联机游戏。
