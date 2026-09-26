using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// =====================================================================
// Light In Dark 模组验证服务器（握手票据签发）
//
// 用途：
//   玩家客户端把 (version, apiHash, modHash, accountId) 发给本服务器，
//   服务器核对是否与官方发布 hash 一致，一致则用 ECDSA P-256 私钥
//   对票据内容签名并返回；房主用内置公钥验签，从而确认对方
//   "是官方未篡改的客户端"。
//
// 端点：
//   GET  /health        存活检查
//   GET  /pubkey        返回公钥 PEM（供编译进模组）
//   POST /verify        票据签发 {version, apiHash, modHash, accountId}
//
// 配置：
//   首次运行自动生成 ECDSA P-256 密钥对（private.pem / public.pem）。
//   官方 hash 列表放同目录 official.json：
//     { "versions": [ { "version": "1.0.0", "apiHash": 123, "modHash": 456 } ] }
//   票据有效期 24 小时（与客户端缓存一致）。
// =====================================================================

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);

// ---------- 密钥管理 ----------
string privateKeyPath = Path.Combine(dataDir, "private.pem");
string publicKeyPath = Path.Combine(dataDir, "public.pem");

using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
if (File.Exists(privateKeyPath))
{
    ecdsa.ImportFromPem(File.ReadAllText(privateKeyPath));
}
else
{
    ecdsa.GenerateKey(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(privateKeyPath, ecdsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicKeyPath, ecdsa.ExportSubjectPublicKeyInfoPem());
}

// ---------- 官方 hash 表 ----------
string officialPath = Path.Combine(dataDir, "official.json");
if (!File.Exists(officialPath))
{
    var sample = new
    {
        versions = new object[]
        {
            new { version = "1.0.0", apiHash = 0, modHash = 0 }
        }
    };
    File.WriteAllText(officialPath, JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true }));
}

OfficialHashes LoadOfficial()
{
    try
    {
        var doc = JsonDocument.Parse(File.ReadAllText(officialPath));
        var list = new List<OfficialVersion>();
        foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
        {
            list.Add(new OfficialVersion
            {
                Version = v.GetProperty("version").GetString() ?? "",
                ApiHash = v.GetProperty("apiHash").GetInt32(),
                ModHash = v.GetProperty("modHash").GetInt32(),
            });
        }
        return new OfficialHashes { Versions = list };
    }
    catch (Exception)
    {
        return new OfficialHashes { Versions = new List<OfficialVersion>() };
    }
}

// ---------- 票据编码 ----------
// payload = [1B accountIdLen][accountId utf8][1B versionLen][version utf8][4B nonce][4B apiHash][4B modHash][8B exp(unix)]
// ticket  = base64( payload || 签名(64B, P-256 DER) )
static byte[] BuildPayload(string accountId, string version, int nonce, int apiHash, int modHash, long exp)
{
    var acc = Encoding.UTF8.GetBytes(accountId ?? "");
    var ver = Encoding.UTF8.GetBytes(version ?? "");
    var buf = new byte[1 + acc.Length + 1 + ver.Length + 4 + 4 + 4 + 8];
    int o = 0;
    buf[o++] = (byte)Math.Min(255, acc.Length);
    Array.Copy(acc, 0, buf, o, acc.Length); o += acc.Length;
    buf[o++] = (byte)Math.Min(255, ver.Length);
    Array.Copy(ver, 0, buf, o, ver.Length); o += ver.Length;
    WriteInt32(buf, ref o, nonce);
    WriteInt32(buf, ref o, apiHash);
    WriteInt32(buf, ref o, modHash);
    WriteInt64(buf, ref o, exp);
    return buf;
}

static void WriteInt32(byte[] buf, ref int o, int v)
{
    buf[o++] = (byte)(v & 0xFF);
    buf[o++] = (byte)((v >> 8) & 0xFF);
    buf[o++] = (byte)((v >> 16) & 0xFF);
    buf[o++] = (byte)((v >> 24) & 0xFF);
}

static void WriteInt64(byte[] buf, ref int o, long v)
{
    for (int i = 0; i < 8; i++)
        buf[o++] = (byte)((v >> (8 * i)) & 0xFF);
}

// ---------- 端点 ----------

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }));

app.MapGet("/pubkey", () => Results.Text(File.ReadAllText(publicKeyPath), "text/plain"));

app.MapPost("/verify", (VerifyRequest req) =>
{
    if (req == null || string.IsNullOrEmpty(req.version))
        return Results.BadRequest(new { reason = "missing version" });

    var official = LoadOfficial();
    var match = official.Versions.FirstOrDefault(v =>
        v.Version == req.version && v.ApiHash == req.apiHash && v.ModHash == req.modHash);

    if (match == null)
        return Results.Json(new { ok = false, reason = "hash mismatch" }, statusCode: StatusCodes.Status403Forbidden);

    long exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 24 * 3600; // 24h 有效
    var payload = BuildPayload(req.accountId ?? "", req.version, req.nonce, req.apiHash, req.modHash, exp);
    byte[] sig;
    try
    {
        sig = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
    }
    catch (Exception)
    {
        return Results.Json(new { ok = false, reason = "sign fail" }, statusCode: StatusCodes.Status500InternalServerError);
    }

    var ticket = Convert.ToBase64String(payload.Concat(sig).ToArray());
    return Results.Ok(new { ok = true, ticket, exp });
});

app.Run();

// =====================================================================

public class VerifyRequest
{
    public string version { get; set; }
    public int apiHash { get; set; }
    public int modHash { get; set; }
    public string accountId { get; set; }
    public int nonce { get; set; }
}

public class OfficialVersion
{
    public string Version { get; set; }
    public int ApiHash { get; set; }
    public int ModHash { get; set; }
}

public class OfficialHashes
{
    public List<OfficialVersion> Versions { get; set; } = new();
}
