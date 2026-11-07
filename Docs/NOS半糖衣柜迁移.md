# NOS 半糖衣柜迁移

此变更把用户提供的 NOS 包迁入 LID：59 顶帽子、34 件面饰，共 93 件。
装扮默认内置于 `Light.dll`，打开原版帽子／面饰页，在分类中选择“半糖的衣柜”。
安装时将 `Output/Light.dll` 与 `Output/LightInDark.dll` 一起替换到测试游戏的
`BepInEx/plugins/`。所有客户端需使用相同构建，才能识别相同的装扮 ID。

## 迁移内容

- 转换 `addon.meta`、`MoreCosmic/Contents.json` 为 LID 的 `AddonInfo.json`、`CosmicInfo.json`。
  NOS 的大写布尔值及正则转义名称在转换阶段处理；名字、作者、原分组保留。
- 194 个实际引用的 PNG 原样保留；缺少地址别名的图片按原清单 MD5 找回。
  同时兼容旧 NOS 未补零的 MD5 写法。缺失或校验失败的图片使转换失败，不静默漏件。
- ID 根据类别、原作者和原名生成，符合 LID 的小写字符规则；不随 ZIP 内文件顺序改变。
- 保留 NOS 的前后层、朝向替代图、行走、上下梯和进出管道图，以及 Bounce、BehindHat 标志。
  6 件只有背部素材的装扮继续显示在身体背后，不改成前景图。
- 保留 NOS 的 112.875 PPU 和 `(0.53, 0.575)` 轴心。76 件固定色使用原图；
  17 件 Adaptive 使用原版玩家双色材质和实际穿戴者颜色。
  旧 LID `colorMode: player` 的红／蓝遮罩语义保持不变。
- 用内存 AddressableAsset 包装接入原版缓存、穿戴、材质与预览流程。
  新增背部面饰渲染器继承原渲染器的层、遮罩、颜色和显隐，换回原版面饰后清理显示。
  相同路径的贴图复用缓存，缓存命中检查 Unity 资源是否仍存活。

转换工具针对本包用到的静态图片字段；不支持的动画网格或额外遮罩字段会明确报错，
不声称兼容所有 NOS 服装包。此包没有皮肤、名牌或多帧图片。

## 自动验证

需要 .NET 10 SDK、Python 3，以及仓库 `Libs/` 中的现有依赖：

```bash
DOTNET=dotnet bash Tools/run-cosmetics-tests.sh
```

结果：4 项 Python 测试、28 项 C# 检查通过；Debug、Release 均 0 error。
Python 逐件验证原名称、作者、Adaptive 标志、所有引用图片的 MD5、PNG 块 CRC 和解压数据，
并测试转换可复现、非标准 JSON 以及缺图失败。
C# 测试直接编译生产加载器和状态选择器，验证内置加载、坏包隔离、重复包、关闭 ZIP 后可用、
旧 LID 包兼容、空作者、背部装扮、朝向和动画状态。
另用 Cecil 验证 Release DLL 确实嵌入 ZIP、各补丁有类级 HarmonyPatch，以及资源包装方法存在。

已有 `Tools/run-regressions.sh` 会先运行以上装扮检查，随后运行历史回归项目。
本次尝试历史项目发现已有测试夹具未跟上主分支接口：Regression 缺少新版提示框方法及
HudLayoutSettings，Gameplay 缺少 TMP_FontAsset，Rpc 缺少 LocalPlayer／Modifiers，
Lobby 缺少 LobbyBehaviour，ApiConsumer 示例未实现新增职业属性。
在未修改的上游 `dde3621` 独立工作区重现以上五个项目的相同编译错误（其中 Regression 为 6 个）；
本次没有改动这些失败所涉及的生产代码和测试夹具。

## 实机验证边界

当前 Linux 工作区没有运行中的 Among Us／Unity，无法验证 IL2CPP 注入、实际帧渲染、
衣柜点击区域或双客户端同步。自动检查通过不代表这些实机项目通过。

实机验收时请依次检查：

1. 主菜单和大厅打开衣柜，切换原版／半糖的衣柜，两页分别显示 59 顶帽子、34 件面饰。
2. 选择“光环”“超大蝴蝶结”“太玄之刃”等仅背部装扮，再换回原版，检查层级与残留。
3. 装备普通、Adaptive 和左右不对称装扮，改变玩家颜色和朝向；另一客户端应显示相同外观。
4. 行走、上下梯、进出管道，检查对应图片与回到待机的恢复；切换场景后重新打开衣柜。
5. 会议、隐身、死亡和换装时检查面饰背部没有单独残留，查看 LightLog.log／Player.log 异常。

素材来源与作者说明见 `Light/Resources/CosmicAddons/README.md`。
