# 内置装扮包

`halfsugarscloset.zip` 由用户提供的 NOS 服装包 `half sugar's closet(9.12).zip`
转换而来，包名“半糖的衣柜”，原包作者“曦曦”，版本 1.0。
包含 59 顶帽子、34 件面饰；保留每件装扮自己的名字、作者和来源分组。

原始上传文件 SHA-256：
`5e3a2593a714e1aa5158b521355d13a40e4cd7900745c0a151076d6fbbf93911`

ZIP 内的 `Migration.json` 记录原名称到 LID 稳定 ID 的映射；
`Source/addon.meta`、`Source/Contents.json` 保留规范化后的 NOS 元数据。
全部 194 个被引用的 PNG 保持原始字节，没有重新绘制或重新上色。
原包中另有 22 个未被清单引用的 PNG，不属于本次加载的 93 件装扮。

这些是第三方美术素材，版权与许可归各自权利人所有；
参见仓库 `LICENSE-ASSETS` 第 4 条。代码的 GPL 许可不授予额外美术使用权。

转换命令（Python 3 标准库，无额外依赖）：

```bash
python3 Tools/migrate-nos-cosmetics.py "half sugar's closet(9.12).zip" \
  Light/Resources/CosmicAddons/halfsugarscloset.zip
```

该 ZIP 由主插件嵌入并直接读取，安装两个插件 DLL 即可使用，
无需把源 NOS 包另放入 `CosmicAddons/`。
外置 LID 原生装扮包仍从游戏根目录 `CosmicAddons/*.zip` 读取；
同 ID 的外置包不会重复注册内置装扮。
