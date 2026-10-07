using BepInEx.Unity.IL2CPP.Utils;
using Il2CppInterop.Runtime.Injection;
using InnerNet;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.RPCs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LightInDark.Utilities;

/// <summary>
/// 静态工具类。
/// </summary>
public static class LightUtils
{
    #region 任务数量

    /// <summary>
    /// 取某个玩家**当前的任务数量**（默认取本地玩家）。
    ///
    /// 用途：职业想按"现在的任务数"做判断时用（例如"任务做完了给奖励"、
    /// "每完成一个任务获得 X"、技能强度随任务数变化…）。
    ///
    /// ⚠️ 任务表是在 **`ShipStatus.Begin`** 里发的（`ShipStatus.cs:421 RpcSetTasks`），
    ///    所以大厅/选人阶段读到的会是 0 ✓（那时候还没发任务）。
    ///
    /// ⚠️ `countOnlyProgress` 的含义：中立/特殊职业的 <c>Data.TasksCountTowardProgress</c> 会被我们置成 false
    ///    （见 `RpcDefinitions.ApplyRole`）——那种玩家的任务**不计入团队进度**，
    ///    如果你想问"还差几个任务才算赢"，要用 `countOnlyProgress: true` ✓
    /// </summary>
    /// <param name="player">目标玩家；null = 本地玩家</param>
    /// <param name="countOnlyProgress">true 时只在"计入进度"时才算（默认 false = 全部任务）</param>
    public static int GetCurrentTaskCount(PlayerControl player = null, bool countOnlyProgress = false)
    {
        try
        {
            var pc = player;
            if (pc == null) pc = PlayerControl.LocalPlayer;      // == 走 UnityEngine.Object 重载（AGENTS §4.6.1）
            if (pc == null) return 0;

            var data = pc.Data;
            if (data == null) return 0;

            if (countOnlyProgress)
            {
                // ⚠️ `TasksCountTowardProgress` 在 **RoleBehaviour** 上（不是 NetworkedPlayerInfo）——
                //    我们给中立职业置 false 的那一处就是这么写的（RpcDefinitions.ApplyRole）
                var role = data.Role;
                if (role == null || !role.TasksCountTowardProgress) return 0;
            }

            var tasks = data.Tasks;
            if (tasks == null) return 0;
            return tasks.Count;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[LightUtils.GetCurrentTaskCount] {ex.Message}");
            return 0;
        }
    }

    #endregion

    #region AttachComponent
    /// <summary>
    /// 将任意组件类挂到 GameObject 上。
    /// 未指定目标对象时默认新建一个空对象（以组件类型名命名），也可指定挂到其他对象上。
    /// 返回附加组件完毕的 GameObject。创建失败时输出警告并重试，直到成功。
    /// </summary>
    /// <typeparam name="T">要附加的组件类型（MonoBehaviour / Component）</typeparam>
    /// <param name="target">目标 GameObject；为 null 时自动创建一个空对象</param>
    /// <param name="objectName">自动创建对象时的名称；为 null 时使用组件类型名</param>
    /// <returns>附加组件完毕的 GameObject</returns>
    public static GameObject AttachComponent<T>(GameObject target = null, string objectName = null) where T : Component
    {
        while (true)
        {
            try
            {
                if (target == null)
                    target = new GameObject(objectName ?? typeof(T).Name);

                if (target.GetComponent<T>() == null)
                    target.AddComponent<T>();

                return target;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[LightUtils.AttachComponent] 挂载 {typeof(T).Name} 失败：{ex.Message}，正在重试...");
                try
                {
                    ClassInjector.RegisterTypeInIl2Cpp<T>();
                }
                catch
                {

                }
                System.Threading.Thread.Sleep(50);
            }
        }
    }
    #endregion

    #region FlashScreen
    private static GameObject _flashObject;
    private static SpriteRenderer _renderer;
    private static Coroutine _currentCoroutine;
    private static MonoBehaviour _coroutineHost;

    private static readonly Color DefaultColor = Color.Red;
    private const float DefaultFadeIn = 0f;
    private const float DefaultHold = 0.15f;
    private const float DefaultFadeOut = 0.3f;

    class CoroutineHost : MonoBehaviour { }
    public static void PlayFlash(Color color,float fadeIn,float hold,float fadeOut)
    {
        try
        {
            if (_coroutineHost == null)
            {
                var go = new GameObject("ScreenFlashCoroutineHost");
                Object.DontDestroyOnLoad(go);
                _coroutineHost = go.AddComponent<CoroutineHost>();
            }
            if (_currentCoroutine != null)
                _coroutineHost.StopCoroutine(_currentCoroutine);
            if (_flashObject == null)
            {
                _flashObject = new GameObject("ScreenFlash");
                Object.DontDestroyOnLoad(_flashObject);
                _flashObject.transform.SetParent(null);

                _renderer = _flashObject.AddComponent<SpriteRenderer>();
                var texture = Texture2D.whiteTexture;
                _renderer.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new UnityEngine.Vector2(0.5f, 0.5f));
                _renderer.sortingOrder = 999999999;
                _renderer.gameObject.layer = LayerMask.NameToLayer("UI");
            }
            _currentCoroutine = _coroutineHost.StartCoroutine(FlashCoroutine(color, fadeIn, hold, fadeOut));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LightUtils.PlayFlash", ex);
        }
    }

    public static void PlayFlash()
    {
        try
        {
            PlayFlash(DefaultColor, DefaultFadeIn, DefaultHold, DefaultFadeOut);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LightUtils.PlayFlash", ex);
        }
    }

    public static void PlayFlash(Color color)
    {
        try
        {
            PlayFlash(color, DefaultFadeIn, DefaultHold, DefaultFadeOut);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LightUtils.PlayFlash", ex);
        }
    }

    static IEnumerator FlashCoroutine(Color color, float fadeIn, float hold, float fadeOut)
    {
        _renderer.color = new Color(color.R, color.G, color.B, 0f).ToUnityColor();
        _renderer.gameObject.SetActive(true);
        if (fadeIn > 0f)
        {
            float t = 0f;
            while (t < fadeIn)
            {
                t += Time.deltaTime;
                float alpha = Mathf.Clamp01(t / fadeIn);
                _renderer.color = new Color(color.R, color.G, color.B, alpha).ToUnityColor();
                yield return null;
            }
        }
        _renderer.color = new Color(color.R, color.G, color.B, 1f).ToUnityColor();

        if (hold > 0f)
            yield return new WaitForSeconds(hold);
        if (fadeOut > 0f)
        {
            float t = 0f;
            while (t < fadeOut)
            {
                t += Time.deltaTime;
                float alpha = 1f - Mathf.Clamp01(t / fadeOut);
                _renderer.color = new Color(color.R, color.G, color.B, alpha).ToUnityColor();
                yield return null;
            }
        }
        _renderer.color = new Color(color.R, color.G, color.B, 0f).ToUnityColor();
        _renderer.gameObject.SetActive(false);
        _currentCoroutine = null;
    }
    #endregion
    #region TREE
    public static BigInteger TREE(int n)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));

        BigInteger best = 0;
        var seq = new List<(int[] label, int[] parent)>();

        void Search()
        {
            if (seq.Count > best) best = seq.Count;

            int nextIndex = seq.Count + 1; // 下一棵树是第 nextIndex 棵，大小 <= nextIndex

            foreach (var candidate in GenerateTrees(n, nextIndex))
            {
                bool ok = true;

                foreach (var old in seq)
                {
                    if (Embeds(old, candidate))
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok) continue;

                seq.Add(candidate);
                Search();
                seq.RemoveAt(seq.Count - 1);
            }
        }

        IEnumerable<(int[] label, int[] parent)> GenerateTrees(int colors, int maxSize)
        {
            for (int size = 1; size <= maxSize; size++)
                foreach (var t in GenerateTreesOfSize(colors, size))
                    yield return t;
        }

        IEnumerable<(int[] label, int[] parent)> GenerateTreesOfSize(int colors, int size)
        {
            foreach (var parent in GenerateParents(size))
                foreach (var label in GenerateLabels(colors, size))
                    yield return ((int[])label.Clone(), (int[])parent.Clone());
        }

        IEnumerable<int[]> GenerateParents(int size)
        {
            if (size == 1)
            {
                yield return new[] { -1 };
                yield break;
            }

            var parent = new int[size];
            parent[0] = -1;

            foreach (var p in GenerateParentsRec(parent, 1))
                yield return p;
        }

        IEnumerable<int[]> GenerateParentsRec(int[] parent, int idx)
        {
            if (idx == parent.Length)
            {
                yield return (int[])parent.Clone();
                yield break;
            }

            for (int p = 0; p < idx; p++)
            {
                parent[idx] = p;
                foreach (var r in GenerateParentsRec(parent, idx + 1))
                    yield return r;
            }
        }

        IEnumerable<int[]> GenerateLabels(int colors, int size)
        {
            var label = new int[size];

            foreach (var l in GenerateLabelsRec(label, 0, colors))
                yield return l;
        }

        IEnumerable<int[]> GenerateLabelsRec(int[] label, int idx, int colors)
        {
            if (idx == label.Length)
            {
                yield return (int[])label.Clone();
                yield break;
            }

            for (int c = 0; c < colors; c++)
            {
                label[idx] = c;
                foreach (var r in GenerateLabelsRec(label, idx + 1, colors))
                    yield return r;
            }
        }

        bool Embeds((int[] label, int[] parent) a, (int[] label, int[] parent) b)
        {
            if (a.label.Length > b.label.Length) return false;
            if (a.label[0] != b.label[0]) return false; // 有根树：根必须映到根

            int na = a.label.Length;
            int nb = b.label.Length;

            // anc[u, v] == true 表示 u 是 v 的祖先（包含自己）
            var anc = new bool[nb, nb];
            for (int v = 0; v < nb; v++)
            {
                int cur = v;
                while (cur != -1)
                {
                    anc[cur, v] = true;
                    cur = b.parent[cur];
                }
            }

            var map = new int[na];
            for (int i = 0; i < na; i++) map[i] = -1;

            map[0] = 0;
            return Match(a, b, anc, 1, map);
        }

        bool Match(
            (int[] label, int[] parent) a,
            (int[] label, int[] parent) b,
            bool[,] anc,
            int ai,
            int[] map)
        {
            if (ai == a.label.Length) return true;

            for (int bi = 0; bi < b.label.Length; bi++)
            {
                if (map.Contains(bi)) continue;
                if (a.label[ai] != b.label[bi]) continue;

                bool ok = true;

                for (int aj = 0; aj < ai; aj++)
                {
                    int bj = map[aj];

                    if (IsAncestor(a.parent, aj, ai) && !anc[bj, bi])
                    {
                        ok = false;
                        break;
                    }

                    if (IsAncestor(a.parent, ai, aj) && !anc[bi, bj])
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok) continue;

                map[ai] = bi;
                if (Match(a, b, anc, ai + 1, map)) return true;
                map[ai] = -1;
            }

            return false;
        }

        bool IsAncestor(int[] parent, int u, int v)
        {
            int cur = v;
            while (cur != -1)
            {
                if (cur == u) return true;
                cur = parent[cur];
            }
            return false;
        }

        Search();
        return best;
    }
    #endregion
    public static ClientData? GetClient(PlayerControl player)
    {
        try
        {
            return AmongUsClient.Instance.allClients
                .ToArray().FirstOrDefault(cd => cd.Character?.PlayerId == player.PlayerId);
        }
        catch { return null; }
    }
    public static void KickPlayer(Player p, string kickerName,string reason = null)
    {
        try
        {
            int i = AmongUsClient.Instance.GameId;
            string code = GameCode.IntToGameNameV2(i);

            foreach(var player in PlayerControl.AllPlayerControls)
                player.RpcSendChat($"<color=red>玩家 {p.Control.Data.PlayerName} 被踢出房间，原因：{reason ?? "无"}</color>");

            string realReason = reason ?? "无";
            string kickerDisplay = string.IsNullOrEmpty(kickerName) ? "" : $"<b>{kickerName}</b>";
            string prefix = $"你被{kickerDisplay}踢出了 {code} 。\n原因：{realReason}";
            RpcDefinitions.KickPlayerWithReason(p.Control.PlayerId, prefix);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LightUtils.KickPlayer", ex);
        }
    }
    public static class KickManager
    {
        public static string kickReason = string.Empty;
        public static float kickReasonWaitUntil = 0f;
        public static float kickReasonConsumeUntil = 0f;

        public static void Clear()
        {
            try
            {
                kickReason = string.Empty;
                kickReasonWaitUntil = 0f;
                kickReasonConsumeUntil = 0f;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("KickManager.Clear", ex);
            }
        }
    }
    /// <summary>
    /// 将PlayerControl转换为LightInDark.Game.Player。
    /// </summary>
    /// <returns></returns>
    public static Player ToLIDPlayer(this PlayerControl pc)
    {
        try
        {
            if (pc == null) return null;
            return Game.GameManager.Instance?.GetPlayer(pc.PlayerId);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LightUtils.ToLIDPlayer", ex);
            return null;
        }
    }


    /// <summary>
    /// 展示一个类似于断开连接的弹窗，显示自定义文本。
    /// </summary>
    /// <param name="text">要显示的文本</param>
    public static void ShowCustomDisconnectWindow(string text)
    {
        try
        {
            var popup = DestroyableSingleton<DisconnectPopup>.Instance;
            if (popup != null)
            {
                popup._textArea.text = text;
                popup.OnTextChanged();
                popup.gameObject.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LightUtils.ShowCustomDisconnectWindow", ex);
        }
    }
    /// <summary>
    /// 关闭ShowCustomDisconnectWindow(string text)的窗口。
    /// </summary>
    public static void CloseCustomDisconnectWindow()
    {
        try
        {
            var popup = DestroyableSingleton<DisconnectPopup>.Instance;
            popup?.gameObject.SetActive(false);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LightUtils.CloseCustomDisconnectWindow", ex);
        }
    }

    /// <summary>
    /// 检查当前是否为自定义服务器。
    /// </summary>
    /// <returns>如果是自定义服务器，返回true；否则，返回false</returns>
    public static bool IsCustomServer()
    {
        try
        {
            return ServerManager.Instance?.CurrentRegion.TranslateName is StringNames.NoTranslation or null;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LightUtils.IsCustomServer", ex);
            return default;
        }
    }
    /// <summary>
    /// 检查当前是否在大厅中。
    /// </summary>
    /// <returns>在大厅中时，返回true；否则，返回false</returns>
    public static bool IsInLobby()
    {
        try
        {
            return LobbyBehaviour.Instance != null;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LightUtils.IsInLobby", ex);
            return default;
        }
    }
    /// <summary>
    /// 检测今天是不是四月一号。
    /// </summary>
    /// <returns></returns>
    public static bool IsAprilDay()
    {
        var today = DateTime.Now;
        return today.Month == 4 && today.Day == 1;
    }
}
