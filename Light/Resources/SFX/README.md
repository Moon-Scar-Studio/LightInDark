# 音效资源目录 (SFX)

将此目录下的 mp3 文件通过 `Light\LightPluginMain.csproj` 的
`<EmbeddedResource Include="Resources\**\*.*" />` 打包进 Light.dll，
运行时由 `LightInDark.Audio.SfxManager.Play(path)` 按相对路径映射加载。

相对路径规则：`./Resources/SFX/xxx.mp3` → 嵌入资源名 `Light.Resources.SFX.xxx.mp3`

## 当前代码引用的音效（放入后重新构建 Light 项目即可生效）

| 相对路径 | 用途 | 引用处 |
|---|---|---|
| `./Resources/SFX/CallerIntro.mp3` | Caller 开场音效（替换原版） | `Light\Roles\Crewmates\Caller.cs` → `IntroSFX` |
| `./Resources/SFX/CallerUse.mp3` | Caller 按钮点击音效 | `Caller.cs` → `SetOnClickSFX` |
| `./Resources/SFX/CooldownReady.mp3` | 按钮冷却完成音效 | `Caller.cs` → `SetCooldownReadySFX` |

## 调用方行为

- 传入路径为 null / 空 / 空白：`SfxManager.Play` 静默忽略。
- 路径合法但资源不存在：打日志警告，不播放。
- 资源存在：首次调用异步解码（UnityWebRequest 原生 mp3 解码，临时落盘到
  `%TEMP%\LightInDark\SFX`），完成后自动播放；之后复用缓存，同步直出。