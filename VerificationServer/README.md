# Light In Dark 握手验证系统（反篡改）

模组与服务器配合的**签名验证握手**,用于在大厅中检测"被篡改的 LightAPI / Light.dll"。

## 原理

```
房主 ──Challenge RPC(一次性 nonce)──▶ 玩家
玩家 ──POST /verify(version+hash×2+accountId+nonce)──▶ 验证服务器
服务器 ──合法则 ECDSA P-256 私钥签名票据──▶ 玩家
玩家 ──Handshake RPC(上报 hash×2 + 票据)──▶ 房主
房主验签(内置公钥) + nonce 匹配 + hash 与本地一致 + accountId 匹配
  └─ 任一失败 → 原生右下角提示 / 延迟踢出
```

**为什么防篡改**:篡改 dll → 文件 hash 变 → 服务器 `official.json` 里没有该 hash → 拒签 → 玩家拿不到合法票据 → 房主验签失败。改代码容易,但**没有服务器私钥签不出有效票据**;改 hash 上报也会被"票据内 hash ≠ 上报 hash"拆穿;借别人票据会被 nonce(每局一次性)+ accountId(绑定账号身份)识破。

**已知边界**:票据缓存 24h,服务器不可达时宽限(不拦截);纯离线/故意不发握手的玩家靠"12 秒超时提示"兜底,但理论上无法 100% 拦截所有本地伪造(MonoMod 级修改模组本身)。这是"反普通篡改 + 提高绕过成本"的务实方案。

---

## 一、部署验证服务器

### 1. 生成密钥对(首次运行自动生成)
```bash
dotnet run -c Release --project VerificationServer --urls http://0.0.0.0:58080
```
首次运行会在 `bin/Release/net10.0/data/` 生成:
- `private.pem` — **私钥,绝对不要泄露,不要放进模组**
- `public.pem` — 公钥,供编译进模组
- `official.json` — 官方 hash 白名单(初始是样例)

### 2. 写入官方 hash(每次发版必做!)
1. 构建出 `Light.dll` 与 `LightInDark.dll` 后,计算两者的 hash(见下"计算 hash")
2. 编辑 `data/official.json`:
```json
{
  "versions": [
    { "version": "1.0.0", "apiHash": -871552443, "modHash": 1180251441 }
  ]
}
```
> **警告**:不更新此文件,新版本玩家会被判"版本不匹配"。这是最大的运营陷阱。

### 3. 部署到服务器
- 发布: `dotnet publish -c Release`
- 建议用 Nginx/反代加 HTTPS(票据内容会签名但明文传输,HTTPS 防窃听/中间人)
- `data/` 目录持久化(密钥、official.json 都在里面),重启不丢失
- 健康检查: `GET /verify` 服务器地址 + `/health`

### 4. 把公钥写进模组
`GET <服务器>/pubkey` 返回 PEM,替换
`LightAPI/Handshake/HandshakeCrypto.cs` 里的 `PublicKeyPem` 常量,重新构建。

---

## 二、计算 hash(发版脚本)

hash = 文件 SHA-256 前 4 字节转 int32(与模组 `HandshakeCrypto.ComputeFileHash` 一致)。

```powershell
$api = Get-FileHash 'Output\LightInDark.dll' -Algorithm SHA256
$mod = Get-FileHash 'Output\Light.dll' -Algorithm SHA256
# 取前 4 字节 → int32(小端)
[BitConverter]::ToInt32([Convert]::FromHexString($api.Hash)[0..3], 0)
[BitConverter]::ToInt32([Convert]::FromHexString($mod.Hash)[0..3], 0)
```
建议做成 `build.ps1`:构建 → 算 hash → 提示填到 `official.json`。**每次发版手动时容易忘,务必自动化。**

---

## 三、模组侧配置(玩家的 Settings.json)

`LightUserDataPath/Settings.json`(与 MaxFPS 同文件):

```json
{
  "VerifyServerUrl": "https://your-server.com",
  "HandshakeMode": 0
}
```

| 字段 | 值 | 效果 |
|---|---|---|
| `VerifyServerUrl` | 空(默认) | **整个握手禁用** |
| | `https://...` | 启用:进大厅自动挑战-应答验证 |
| `HandshakeMode` | `0`(默认) | 仅提示(右下角原生提示) |
| | `1` | 提示后 3 秒踢出 |

**注意**:所有玩家(含房主)都要配置同一个 `VerifyServerUrl` 才有效——房主必须开,玩家不开的话房主收不到握手 → 12 秒超时提示。

---

## 四、不改代码的日常维护

| 场景 | 做法 |
|---|---|
| 发新版本模组 | 更新服务器 `official.json` 的 hash → 玩家自动通过 |
| 换服务器域名 | 改玩家 `Settings.json` 的 `VerifyServerUrl` + 重新嵌入公钥 |
| 被盗私钥 | **立刻**换密钥对:重新生成 → 更新服务器私钥 + 重新嵌入公钥 → 旧玩家票据全失效需重新获取(服务器不可达时会宽限) |
| 想强制所有人用 | `HandshakeMode=1` 只影响房主自己的判断;要全局强制需把默认值改代码或做云端配置下发 |

---

## 五、调试

- 模组日志 `LightLog.log`:`[Handshake]` 开头记录挑战/nonce/票据/验证结果
- 服务器日志:`/verify` 的 200/403
- 常见问题:
  - 大家都"版本不匹配" → 官方 hash 没同步 / 有人用了旧版本 dll
  - 一直"超时未握手" → 服务器地址没配 / 连不上服务器(票据拿不到)→ 检查 `VerifyServerUrl`
  - 公钥不对 → 验签失败 → 重新 `GET /pubkey` 嵌入