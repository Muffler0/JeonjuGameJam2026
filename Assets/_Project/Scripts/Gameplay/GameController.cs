using System;

namespace Project.Gameplay
{
    // =============================================================
    //  【実装のお願い / Implementation request】
    //
    //  このファイルの各メソッドの中身を実装してください。
    //  ルール層は「ゲームの規則」だけを持ち、通信・画面には一切関わりません。
    //
    //  1. UnityEngine を使わないでください。
    //     using UnityEngine; は書かない。純粋な C# のみで書けます。
    //     MonoBehaviour の継承も不要です。
    //
    //  2. UnityEngine.Random ではなく System.Random を使ってください。
    //     ただし、両クライアントで結果が一致する必要がある値
    //     （盤面の配置など）は自分で生成せず、引数で受け取ります。
    //
    //  3. 触るのは Scripts/Gameplay/ の中だけにしてください。
    //     シーンやプレハブは触らないでください（マージ衝突を避けるため）。
    //
    //  4. メソッドのシグネチャは変更しないでください。
    //     変更が必要になったら、先に相談をお願いします。
    //     中の実装は自由です。フィールドやプライベートメソッドの追加も歓迎です。
    //
    //  5. 例外は投げないでください。
    //     不正な入力（自分の番でない、既に選んだスロット等）は
    //     何もせずに return してください。
    //
    //  6. static 変数は使わないでください。
    //     再戦（ResetForNewRound）で状態が残ってしまいます。
    //
    //  7. デバッグ出力が必要なときは Log?.Invoke("...") を使ってください。
    //     Debug.Log は UnityEngine なので使えません。
    //
    //  8. RunSelfTest() に簡単な動作確認を書いておくと、
    //     Unity を触らずに単体で確認できます。
    //
    //  ※ ゲームのルールはまだ確定していない部分があります。
    //     仕様書（別途共有）を正としてください。
    //     疑問点があれば実装を進める前に聞いてください。
    // =============================================================

    /// <summary>
    /// ゲームのルールを持つクラス。
    /// 外部との接点は「入力メソッド」と「通知イベント」の 2 種類だけです。
    ///
    /// 入力  : 外部 -> ルール層（Submit系メソッド）
    /// 通知  : ルール層 -> 外部（On系イベント）
    /// </summary>
    public class GameController
    {
        // ---------------------------------------------------------
        //  通知イベント（ルール層 -> 外部）
        // ---------------------------------------------------------

        /// <summary>お題が提示された。引数は（出題者, スロット番号）</summary>
        public event Action<PlayerId, int> OnTopicPresented;

        /// <summary>判定が 1 回行われた。</summary>
        public event Action<JudgeReport> OnJudged;

        /// <summary>
        /// ゲームが終了した。
        /// 画面遷移はこのイベントでのみ行います。
        /// OnJudged では遷移しないでください（後半の相手にも機会があるため）。
        /// </summary>
        public event Action<GameFinishedInfo> OnGameFinished;

        /// <summary>手番が変わった。</summary>
        public event Action<PlayerId> OnTurnChanged;

        /// <summary>デバッグ用のログ出力口。Unity 側が受け取って表示します。</summary>
        public Action<string> Log;

        // ---------------------------------------------------------
        //  状態の読み取り（外部 -> ルール層）
        // ---------------------------------------------------------

        /// <summary>現在の手番。</summary>
        public PlayerId CurrentTurn { get; private set; }

        /// <summary>ゲームが進行中かどうか。</summary>
        public bool IsPlaying { get; private set; }

        // ---------------------------------------------------------
        //  ゲーム開始 / 終了
        // ---------------------------------------------------------

        /// <summary>
        /// ゲームを開始します。
        ///
        /// 盤面はシードから各自生成するのではなく、
        /// マスター側が作った配列をそのまま両者に配ります。
        /// 環境差で盤面がずれる心配がなくなるためです。
        /// </summary>
        /// <param name="itemIds">スロット順に並んだアイテム ID の配列</param>
        /// <param name="firstPlayer">先手</param>
        public void StartGame(int[] itemIds, PlayerId firstPlayer)
        {
            // TODO: 盤面の初期化、手番の設定、IsPlaying = true
            throw new NotImplementedException();
        }

        /// <summary>
        /// 相手が退出したため、ゲームを中断します。
        /// 待機中・ローディング中・対局中のどのタイミングでも呼ばれ得ます。
        /// 既に終了している場合は何もせずに return してください。
        /// </summary>
        /// <param name="leaver">退出したプレイヤー</param>
        public void AbortGame(PlayerId leaver)
        {
            // TODO: IsPlaying = false にして
            //       OnGameFinished（Reason = OpponentLeft）を発火
            throw new NotImplementedException();
        }

        /// <summary>
        /// 再戦のために状態を初期化します。
        /// シーンは再読み込みせず、このメソッドで初期状態に戻します。
        /// そのため、持ち越してはいけない状態が残らないよう注意してください。
        /// </summary>
        public void ResetForNewRound()
        {
            // TODO: 盤面・手番・判定履歴などをすべてクリア
            throw new NotImplementedException();
        }

        // ---------------------------------------------------------
        //  入力（外部 -> ルール層）
        //  引数は slotIndex(int) と bool のみに限定しています。
        // ---------------------------------------------------------

        /// <summary>
        /// 自分の秘密のアイテムを決定します。
        /// </summary>
        public void SubmitSecretItem(PlayerId player, int slotIndex)
        {
            // TODO
            throw new NotImplementedException();
        }

        /// <summary>
        /// 出題に答えるかどうかの意思表示。
        /// </summary>
        public void SubmitAnswerDecision(PlayerId player, bool willAnswer)
        {
            // TODO
            throw new NotImplementedException();
        }

        /// <summary>
        /// 盤面からアイテムを 1 つ選びます。
        /// 判定結果は OnJudged で通知してください。
        /// </summary>
        public void SubmitItemSelection(PlayerId player, int slotIndex)
        {
            // TODO
            throw new NotImplementedException();
        }

        /// <summary>
        /// 確認ダイアログなどの最終確定。
        /// </summary>
        public void SubmitConfirmation(PlayerId player, bool confirmed)
        {
            // TODO
            throw new NotImplementedException();
        }

        // ---------------------------------------------------------
        //  動作確認用
        // ---------------------------------------------------------

        /// <summary>
        /// Unity を使わずに動作を確認するための入口です。
        /// ここに一通りの流れを書いておくと、単体で検証できます。
        /// 例：StartGame -> SubmitSecretItem -> SubmitItemSelection -> 結果確認
        /// </summary>
        public void RunSelfTest()
        {
            // TODO
        }

        // ---------------------------------------------------------
        //  イベント発火用のヘルパー
        //  （null チェックを毎回書かなくて済むようにしたものです）
        // ---------------------------------------------------------

        protected void RaiseTopicPresented(PlayerId presenter, int slotIndex)
            => OnTopicPresented?.Invoke(presenter, slotIndex);

        protected void RaiseJudged(JudgeReport report)
            => OnJudged?.Invoke(report);

        protected void RaiseGameFinished(GameFinishedInfo info)
            => OnGameFinished?.Invoke(info);

        protected void RaiseTurnChanged(PlayerId next)
            => OnTurnChanged?.Invoke(next);

        protected void SetTurn(PlayerId player)
        {
            CurrentTurn = player;
            RaiseTurnChanged(player);
        }

        protected void SetPlaying(bool playing) => IsPlaying = playing;
    }
}
