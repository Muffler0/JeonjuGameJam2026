using System;

namespace Project.Gameplay
{
    // -------------------------------------------------------------
    //  ルール層で使う共通型
    //  Photon / UnityEngine には一切依存しません。
    // -------------------------------------------------------------

    /// <summary>
    /// プレイヤー識別子。
    /// Photon の ActorNumber との変換は NetworkBridge 側が担当します。
    /// ルール層はこの enum だけを見てください。
    /// </summary>
    public enum PlayerId
    {
        None = 0,
        Player1 = 1,
        Player2 = 2
    }

    /// <summary>
    /// ゲームが終わった理由。
    /// </summary>
    public enum GameEndReason
    {
        /// <summary>通常の決着（勝者あり）</summary>
        Normal = 0,

        /// <summary>引き分け</summary>
        Draw = 1,

        /// <summary>相手が退出したため中断</summary>
        OpponentLeft = 2
    }

    /// <summary>
    /// 判定 1 回分の結果。
    /// </summary>
    [Serializable]
    public struct JudgeReport
    {
        /// <summary>回答したプレイヤー</summary>
        public PlayerId Answerer;

        /// <summary>選択されたスロット番号</summary>
        public int SlotIndex;

        /// <summary>そのスロットにあったアイテム ID</summary>
        public int ItemId;

        /// <summary>正解だったか</summary>
        public bool Correct;
    }

    /// <summary>
    /// ゲーム終了時の情報。
    /// </summary>
    [Serializable]
    public struct GameFinishedInfo
    {
        /// <summary>勝者。引き分け・中断の場合は PlayerId.None</summary>
        public PlayerId Winner;

        /// <summary>終了理由</summary>
        public GameEndReason Reason;
    }
}
