# 交付 Prompt：Light In Dark 模组「临时官网」

> **用法（给委托人）**：把本文件**整份**发给服务端的 DSH（或任意能写文件、能跑命令的编码 agent），
> 它应当能**不依赖本仓库**完成站点。文件里的 `{{TODO:xxx}}` 是需要人工补的占位符，
> 上线前必须集中处理（见附录 A）。
>
> **给 DSH 的元指令**：你是一名资深前端 / 设计工程师。先通读全文，输出一份实施计划
> （页面结构 + 设计决策 + 文件清单），确认无歧义后**一次性产出全部文件**。
> **不要反问**：凡本文未给出的信息，用 `{{TODO:…}}` 占位并登记到 `PLACEHOLDERS.md`。
> 不得编造本文事实清单以外的功能、截图、评价或数据。

---

## 0. 任务与目标

为 Among Us 模组 **Light In Dark（暗中辉）** 搭建一个**临时官网**：

- 用途：给玩家一个"这是什么 / 怎么装 / 去哪下 / 出问题怎么办"的**可信落地页**，同时作为社区入口。
- 生命周期：**临时**（后续可能被正式站替换）→ 追求**一次交付、零维护、静态可托管**，不要后台。
- 成功标准：陌生玩家 30 秒内能完成"看懂 → 下载 → 安装"；手机端可读；国内网络可直开（**不依赖任何境外 CDN**）。

---

## 1. 硬性约束（先读，违反即返工）

1. **纯静态**：只允许 HTML / CSS / 原生 JS。禁止后端、数据库、登录、评论、支付、统计脚本、Cookie。
2. **零外部请求**：不得引用 Google Fonts、jsDelivr、unpkg、CDN、图床、第三方统计、iframe 嵌入。
   字体用系统栈（含中文回退），图片全部本地文件。构建完成后 **F12 Network 里不应出现任何跨域请求**。
3. **无构建依赖**（首选）：直接给可运行的静态文件。若你坚持用 Vite/Astro/Tailwind 等，
   必须**同时提交源码 + 已构建的 `dist/`**，且 `dist/` 能在纯静态服务器上直接打开。
4. **中文为主**：全站简体中文；英文只用于 Logo 副标题、职业英文名、法律声明。
   不做语言切换器（临时站不做 i18n）。
5. **移动优先**：360px 宽无横向滚动；断点建议 640 / 960 / 1280。
6. **可访问性**：语义化标签、`alt`、可见 focus 样式、对比度 ≥ 4.5:1、`prefers-reduced-motion` 降级。
7. **性能预算**：首屏 HTML+CSS ≤ 120KB（不含图片）；全站图片 ≤ 1.2MB；JS ≤ 15KB（可为零）。
8. **不得伪造**：不许写"下载量 10 万""官方认证""已上架 Steam 创意工坊"等未提供的说法；
   不许 AI 生成"游戏截图"冒充实机画面。

---

## 2. 事实清单（站上只能出现这些事实）

### 2.1 项目元信息

| 项 | 值 |
|---|---|
| 中文名 | 暗中辉 |
| 英文名 | Light In Dark（缩写 LID） |
| 一句话定位 | 面向 Among Us 的 **All-Client（全客户端）** 模组：职业、模式、界面、装扮与开发者 API |
| 当前版本 | `0.0.1-alpha.1`（Alpha，**必须显著标注**） |
| 游戏版本 | Among Us **19.0**（x64）、**Steam / Windows** |
| 运行框架 | BepInEx **6.0.0-be.788**（net6.0，**位数必须与游戏一致**：AU 19.0 为 x64） |
| 程序集 | `Light.dll`（主插件）+ `LightInDark.dll`（API，主插件硬依赖） |
| 作者 / 归属 | Moon-Scar 制作室（Moon-Scar Studio） |
| 仓库 | `https://github.com/Moon-Scar-Studio/LightInDark` |
| 下载 | GitHub Releases：`https://github.com/Moon-Scar-Studio/LightInDark/releases`（**本站不托管安装包**） |
| 许可证 | 源代码 **GPL-3.0-only**；**原创美术资源不适用 GPL，保留所有权利**（见 `LICENSE-ASSETS`） |
| 开发声明 | 本项目使用了 AI 工具进行辅助开发 |

### 2.2 可对外宣称的功能（8 条，不多不少）

| # | 功能 | 说明文案要点 |
|---|---|---|
| 1 | **All-Client 全客户端** | 房主与所有加入者安装**同一版本**即可联机；不是房主单方装就生效的模组 |
| 2 | **职业系统** | 3 大阵营、**9 个可分配职业**（详见 2.3），房主可配置每个职业的数量与出现概率 |
| 3 | **游戏模式** | 标准模式 / 自由练习；模式框架可扩展（第三方可注册自定义模式） |
| 4 | **局内界面** | 技能按钮、会议名牌按钮、悬停详情框、`H` 键帮助菜单（职业一览 / 我的职业 / 搜索） |
| 5 | **规则与配置** | 大厅「规则编辑」界面内嵌 **MOD 设置** 页签，按阵营分类浏览并调整职业参数 |
| 6 | **新闻与公告** | 游戏内新闻面板；版本更新后可自动提示新公告 |
| 7 | **自定义装扮** | 把装扮 zip 放进 `CosmicAddons/` 即可加载自定义帽子（持续扩展中，**不要写"皮肤/名牌已完成"**） |
| 8 | **开发者 API** | `LightInDark.dll` 提供职业、事件、RPC、能力按钮、配置、Buff、外观覆盖等接口（**标注：接口仍在演进**） |

附加小功能（可放"更多"列表，措辞保守）：音乐播放器（`F3` 开关，支持本地曲库）、
聊天增强（富文本 / 历史记录）、会议中查看任务面板、断线原因提示、地图配色。

### 2.3 职业表（**逐字使用**，颜色用于卡片描边/标签）

> 说明：另有内部调试职业 `Tester`，**不对外展示**。下表 9 个为玩家可见职业。

| 阵营 | 中文名 | 英文名 | 一句话定位 | 主题色 |
|---|---|---|---|---|
| 船员 | 召集者 | Caller | 可随时召开紧急会议 | `#FFFF00` |
| 船员 | 警长 | Sheriff | 击杀内鬼与中立；误杀船员则自己走火身亡 | `#FFFF00` |
| 船员 | 正义的赌怪 | Nice Guesser | 在会议中猜测他人身份，猜错自己死亡 | `#FFFF00` |
| 船员 | 诱饵 | Bait | 被击杀时，凶手会立即报告你的尸体 | `#00FFFF` |
| 内鬼 | 嗜血杀手 | Bloodthirsty Killer | 击杀没有冷却时间 | `#FF0000` |
| 内鬼 | 邪恶的赌怪 | Evil Guesser | 在会议中猜测他人身份，猜错自己死亡 | `#FF1919` |
| 内鬼 | 隐蔽者 | Camouflager | 短时间内让所有人的外观变为小黑人，混淆视野 | `#FF1919` |
| 中立 | 豺狼 | Jackal | 有刀、能钻管道、没有同伴；可拉拢一名跟班，杀光所有人后独自获胜 | `#00A2D3` |
| 中立 | 跟班 | Sidekick | 豺狼的同伙，不能杀人；豺狼死后接替其位置 | `#00A2D3` |

### 2.4 兼容性（必须写清，这是玩家最大的坑）

- 支持：**Steam / Windows / Among Us 19.0（x64）**。
- 本模组**与绝大多数其它模组不兼容**。已知明确不兼容（插件 GUID 层面直接拒绝加载）：

  ```
  Nebula loader (jp.dreamingpig.amongus.nebula.loader)
  Town of Next Edited (com.qin-qwq.townofnextedited)
  SuperNewRoles (jp.ykundesu.supernewroles)
  BetterAmongUs (com.ten.betteramongus)
  EndlessHostRoles (com.gurge44.endlesshostroles)
  Town of Host (com.emptybottle.townofhost)
  FinalSuspect (cn.havenglow.finalsuspect)
  ```
- 游戏版本 / 位数不匹配（32 位 BepInEx 配 64 位游戏）会表现为"插件不加载"，**先查架构**。

### 2.5 链接与联系方式（照抄）

- GitHub：`https://github.com/Moon-Scar-Studio/LightInDark`
- Releases：`https://github.com/Moon-Scar-Studio/LightInDark/releases`
- Issues：`https://github.com/Moon-Scar-Studio/LightInDark/issues/new/choose`
- Discord：`https://discord.gg/E6KjzMEJ3`
- QQ 群：`https://qm.qq.com/q/pzidBWHIxG`
- 邮箱：`hvtXsvc@Outlook.com`
- （以上若与实际不符，以 `{{TODO:确认链接}}` 标注后由人工替换，**不要自行猜测**）

### 2.6 免责与许可（页脚**逐字**使用，中英都要）

> 本模组与 Among Us 或 Innersloth LLC 均无关联，其内容亦未获得 Innersloth LLC 的认可或赞助。本模组包含的部分素材归 Innersloth LLC 所有 © Innersloth LLC
>
> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

许可说明（页脚第二段）：

> 本站与模组源代码采用 **GNU GPL-3.0-only** 授权；`Image/`、`Resources/` 等目录下的原创美术资源**不在 GPL 范围内**，版权归 Moon-Scar Studio 及原画师所有，保留所有权利（All Rights Reserved）。第三方素材版权归各自权利人所有。

---

## 3. 站点结构（单页 + 锚点导航）

一个 `index.html` 承载全部内容，锚点平滑滚动；导航条固定在顶部（移动端折叠菜单）。

区块顺序（不要自行增删）：

1. **Hero**：Logo、站名「暗中辉 / Light In Dark」、一句话定位、版本徽章（`Alpha 0.0.1-alpha.1`）、
   主按钮「下载最新版」→ Releases、次按钮「安装教程」→ `#install`、平台标签（Steam · AU 19.0 · Windows）。
2. **为什么选它（Features）**：2.2 的 8 张卡片（图标用内联 SVG，可用主色描边）。
3. **职业一览（Roles）**：按 3 个阵营分组；每张卡片 = 主题色竖条 + 中文名 + 英文名 + 一句话；
   顶部有「全部 / 船员 / 内鬼 / 中立」筛选（纯前端，`hidden` 切换即可，需键盘可用）。
4. **怎么玩（Modes）**：标准模式 / 自由练习两张卡；说明"配置由房主在规则编辑界面统一调整"。
5. **安装（Install）**：4 步时间线（见 4.3），带"版本必须一致""先关游戏"两个警示条。
6. **常见问题（FAQ）**：6 条折叠面板（见 4.4），`<details>` 原生实现即可。
7. **公告（News）**：3 条卡片（示例内容见 4.5，标注"示例，待替换"）。
8. **社区（Community）**：Discord / QQ 群 / GitHub Issues / 邮箱，四个按钮。
9. **页脚**：法律声明（中英）、许可说明、版本与"本站为临时站点"说明、回到顶部。

导航项：功能 · 职业 · 安装 · FAQ · 公告 · 社区。

---

## 4. 区块文案（可直接抄，措辞不要再创作）

### 4.1 Hero

- 标题：**暗中辉**
- 副标题：`Light In Dark` · Among Us 全客户端模组
- 描述：*为 Among Us 打造的全客户端模组。9 个职业、可扩展的游戏模式、重新设计的局内界面，以及一套给开发者的 API。*
- 徽章：`Alpha` `v0.0.1-alpha.1` `AU 19.0` `Steam / Windows`

### 4.2 功能卡（标题 + 一句说明）

1. **全客户端同步** — 房主与加入者装同一版本即可联机，规则与职业由房主统一裁定。
2. **9 个职业，3 大阵营** — 船员、内鬼、中立各有独特能力；数量与概率可在局外配置。
3. **可扩展的游戏模式** — 标准与自由练习开箱即用，第三方也能注册自己的模式。
4. **重新设计的局内界面** — 技能按钮、会议名牌按钮、悬停详情与 `H` 键帮助菜单。
5. **规则编辑内置 MOD 设置** — 在大厅规则界面里按阵营分类调整每个职业的参数。
6. **游戏内新闻与公告** — 更新后自动提示新公告，不用去翻群聊记录。
7. **自定义装扮** — 把装扮包放进 `CosmicAddons/`，即可加载自定义帽子。
8. **开发者 API** — 职业、事件、RPC、能力按钮、Buff 与外观覆盖，`LightInDark.dll` 直接可用。

### 4.3 安装四步

1. **确认环境**：Steam 版 Among Us **19.0（64 位）**，Windows。
2. **装 BepInEx**：安装 **BepInEx 6.0.0-be.788** 到游戏根目录（**位数选 x64**，与 AU 19.0 一致），先启动一次游戏生成目录结构。
3. **放模组**：把 `Light.dll` 与 `LightInDark.dll` **一起**放进 `BepInEx/plugins/`。
4. **进游戏**：启动游戏，看到模组加载界面即成功；联机时**全员版本必须一致**。

警示条：
- ⚠️ **替换 DLL 前先关闭游戏**，否则文件被占用会复制失败。
- ⚠️ 32 位 / 64 位 BepInEx 装错会表现为"插件完全不加载"，先查架构再看日志。

### 4.4 FAQ（问题 + 答案）

1. **需要所有人都装吗？** 需要。这是 All-Client 模组，房主和加入者都装同一版本。
2. **为什么一进游戏就闪退 / 报错？** 优先检查：游戏是否 19.0、BepInEx 是否为 x64、是否混装了其它模组。
3. **能和 Nebula / ToN / 其它模组一起用吗？** 基本不能。已知不兼容列表见 2.4，插件会被直接拒绝加载。
4. **支持哪些平台？** 目前仅 Steam / Windows；不支持手机、主机与 Microsoft Store 版本。
5. **配置文件在哪？** 存档目录下的 `LightInDark/`；语言文件在游戏根目录 `Language/`，可直接改。
6. **怎么反馈 Bug？** 到 GitHub Issues 提交，并附上 `LightLog.log` 与复现步骤（日志在游戏目录）。

### 4.5 公告（示例 3 条，**页面需标注「示例内容，待替换」**）

| 标签 | 标题 | 摘要 | 日期 |
|---|---|---|---|
| 维护 | 联机服务维护通知 | 10 月 20 日 02:00–04:00 维护，期间联机不可用，单人不受影响。 | 2026-10-18 |
| 公告 | 关于部分 Mod 冲突的说明 | 部分 Mod 改动同一处逻辑会无法进房；建议只保留一个改逻辑的 Mod。 | 2026-10-15 |
| 公告 | 第四季度更新计划 | 模组管理器、房间列表按版本过滤、公告改为服务器下发。 | 2026-10-10 |

> 上线时若已有 GitHub Releases，把本区块改为"最新版本"三张卡（版本号 / 日期 / 主要变更），
> 数据由人工填写，**不要在前端调 GitHub API**。

---

## 5. 视觉规范

- 主题：**深色**、冷静、克制，带一点"暗中发光"的青绿。
- 色板（CSS 变量，必须集中定义）：

  | 变量 | 值 | 用途 |
  |---|---|---|
  | `--bg` | `#0B0F12` | 页面底色 |
  | `--bg-soft` | `#11171C` | 区块交替底色 |
  | `--card` | `#151D23` | 卡片 |
  | `--line` | `#243039` | 描边 / 分割线 |
  | `--text` | `#E8EEF2` | 正文 |
  | `--muted` | `#93A4AF` | 次要文字 |
  | `--primary` | `#4FD1C5` | 强调、按钮、链接 |
  | `--primary-deep` | `#38B2AC` | 按钮渐变深端 / hover |
  | `--warn` | `#F6C445` | Alpha 徽章 / 警示 |
  | `--danger` | `#FF4D4F` | 不兼容提示 |

- 字号：正文 16px / 行高 1.75；`h1` 用 `clamp(2.2rem, 6vw, 4rem)`；区块标题 `clamp(1.5rem, 3vw, 2.25rem)`。
- 字体栈：`system-ui, -apple-system, "Segoe UI", "Microsoft YaHei UI", "Noto Sans SC", sans-serif`。
- 圆角：卡片 16px、按钮 10px、徽章 999px；卡片 1px 描边 + 极轻阴影，**不要重投影**。
- 动效：仅"进入视口淡入 12px 上移"与 hover 亮度变化；`prefers-reduced-motion: reduce` 时全部禁用。
- 装饰：可用 CSS 径向渐变做一层"青绿微光"背景；不要堆贴图、不要粒子、不要自动轮播。
- Logo：使用提供的 `LightInDark.png`（见 6），**不要重新绘制**；在深色底上检查是否清晰。
- 图标：内联 SVG，`stroke="currentColor"`，尺寸 20–24px，禁止引入图标库。

---

## 6. 可用素材（从仓库拷贝，不要外链）

| 文件名（源路径） | 用途 | 备注 |
|---|---|---|
| `Image/LightInDark.png` | 站点 Logo、`og:image` | 105KB，优先用这个 |
| `Light/Resources/Logo/LightInDark.png` | 备用 Logo | 如果与上面是同一张，只放一份 |
| `Light/Resources/RoleImage/CallerImage.png` | 职业区块装饰图（召集者立绘） | 半透明摆放，不裁切变形 |
| `Light/Resources/ModStamp.png` | 页脚小印章 / 装饰 | 小尺寸使用 |

截图位：**目前没有实机截图**。在"功能 / 局内界面"处预留 16:9 占位框（CSS 绘制，标注 `{{TODO:实机截图}}`），
不要用 AI 生成图冒充。

---

## 7. 技术实现要求

- `index.html` + `assets/style.css` + `assets/app.js`（可为空壳），结构清晰、区块加注释。
- 职业筛选、移动端菜单、平滑滚动（`scroll-behavior: smooth`，并 `scroll-margin-top` 让锚点不被导航遮住）。
- 导航在滚动后加 `.scrolled` 类（轻微底噪/描边），确保可读。
- 移动端菜单：`<button aria-expanded>` + 面板，点击锚点自动关闭，Esc 关闭。
- 所有外链 `target="_blank" rel="noopener noreferrer"`。
- `<html lang="zh-CN">`；`<title>`、`<meta name="description">`、OG/Twitter 卡片、`theme-color="#0B0F12"`、favicon（可用 Logo 裁方）。
- 无 JS 时页面仍可读（筛选退化为全部显示，菜单退化为直接铺开）。
- 不使用 `!important` 堆砌；不引入 CSS 框架；不使用 `position: fixed` 做整屏内容。

---

## 8. 交付物（目录结构）

```
lid-site/
├─ index.html
├─ assets/
│  ├─ style.css
│  ├─ app.js
│  ├─ logo.png
│  └─ (可选) caller.png / stamp.png
├─ favicon.svg            # 或 favicon.ico
├─ README.md              # 本地预览 / 部署 / 替换占位符说明
├─ PLACEHOLDERS.md        # 全部 {{TODO:…}} 清单（含出现位置）
├─ nginx.conf.example     # 静态托管示例（含 gzip、缓存头）
└─ Dockerfile             # 可选：nginx:alpine 托管 dist
```

`README.md` 必须包含：
1. 本地预览：`python -m http.server 8080` 或 `npx serve .`；
2. 部署：任意静态托管（Nginx / Caddy / Cloudflare Pages / Vercel）；
3. 如何替换 Logo、公告、链接、版本号（指向具体文件与行）；
4. 明确"本站不托管安装包，下载跳转 GitHub Releases"。

---

## 9. 验收清单（自测后把结果写进 README 末尾）

- [ ] 纯静态打开 `index.html` 可用（`file://` 与 http 两种方式都测）
- [ ] Network 面板**零跨域请求**；离线（断网）刷新后样式与图片正常
- [ ] 375 / 768 / 1440 三档无横向滚动，导航不遮挡锚点
- [ ] 键盘 Tab 可走完全部可交互元素，focus 可见；Esc 可关移动端菜单
- [ ] 9 个职业、8 条功能、6 条 FAQ、3 条公告**数量与文案完全一致**，无错别字
- [ ] 所有外链可点且为新窗口；无 `href="#"` 死链
- [ ] 控制台无 error / warning；无未使用的大文件
- [ ] Lighthouse（移动端）Performance ≥ 90、Accessibility ≥ 90、Best Practices ≥ 90
- [ ] 页脚中英免责声明与许可说明**逐字**存在
- [ ] 全站不出现"官方""认证""下载量""实机截图（伪造）"等违规表述
- [ ] `PLACEHOLDERS.md` 与实际 `{{TODO:…}}` 一一对应，无遗漏

---

## 10. 禁止事项（再强调一次）

- ✗ 后台 / 数据库 / 登录 / 评论 / 支付 / 统计 / Cookie 横幅
- ✗ 任何 CDN、外部字体、外链图片、第三方脚本、iframe
- ✗ 前端调用 GitHub API / 任何 API
- ✗ 编造功能（尤其：皮肤/名牌自定义、成就系统、手机端、官方服务器、模组市场）
- ✗ 伪造截图、伪造评价、伪造下载量
- ✗ 使用与 Among Us 官方视觉混淆的配色/标识（本模组**与 Innersloth 无关**）
- ✗ 把"临时站"做成需要长期维护的工程（无 CI、无依赖升级负担）

---

## 11. 工作流程（按顺序执行）

1. **Plan**：输出站点地图、区块清单、组件拆分、设计要点（不超过 60 行）。
2. **Build**：一次性写出 8 节目录结构里的全部文件（完整内容，不用省略号）。
3. **Verify**：按第 9 节逐条自测，给出"通过 / 不通过 + 证据"。
4. **Handoff**：输出 `README.md` 摘要 + `PLACEHOLDERS.md` + 仍需人工确认的事项（≤5 条）。

---

## 附录 A：必须收集的占位符

| 占位符 | 说明 | 建议默认值 |
|---|---|---|
| `{{TODO:正式域名}}` | canonical / OG url | 暂空，不上线 canonical |
| `{{TODO:最新版本号}}` | Hero 徽章与下载按钮文案 | `0.0.1-alpha.1` |
| `{{TODO:Release 直链}}` | 主下载按钮 | 指向 releases 列表页 |
| `{{TODO:实机截图}}` | 功能区的 16:9 截图 | CSS 占位框 |
| `{{TODO:公告内容}}` | 新闻区三条 | 用 4.5 的示例并标注"示例" |
| `{{TODO:备案/主体信息}}` | 若部署在中国大陆需备案号 | 留空 |
| `{{TODO:确认链接}}` | 仓库 / Discord / QQ / 邮箱 | 用 2.5 的值 |

## 附录 B：需要委托人拍板的 5 件事（交付前确认）

1. **部署位置**：国内服务器（需备案）还是 Cloudflare Pages / Vercel（免备案）？影响是否加 `{{TODO:备案}}`。
2. **下载方式**：只跳 GitHub Releases，还是同时提供国内网盘镜像？（**本站不托管安装包**）
3. **是否放实机截图**：有截图就替换占位框；没有就保持占位，不要生成假图。
4. **公告来源**：先用示例占位，还是等 GitHub Release 文案确定后一次性填入？
5. **是否需要英文版**：默认单语中文 + 英文名/法律声明；若需要全站英文，需追加一轮翻译。


---

*本 prompt 面向"一次交付即可上线"的临时站；如后续要长期运营，另行评估 CMS、统计与多语言方案。*

委托人追加:网页地址为/www/wwwroot/lid.moonscar.cn
