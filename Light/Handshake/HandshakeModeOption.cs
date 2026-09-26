namespace Light.Handshake;

/// <summary>握手验证失败的处理方式。</summary>
public enum HandshakeModeOption
{
    /// <summary>仅显示警告横幅（默认）。</summary>
    Warn = 0,

    /// <summary>直接踢出该玩家。</summary>
    Kick = 1,
}