using System;
using System.Collections.Generic;

namespace Project.Gameplay
{
    // =============================================================
    //  実装メモ（ルール層担当）
    //
    //  ・シグネチャは一切変更していません。
    //  ・UnityEngine 不使用、static 不使用、例外を投げません。
    //  ・不正な入力は Log に理由を出して return します。
    //
    //  【既存の口だけでは表現しきれず、暫定対応した点】
    //
    //  (A) GameFinishedInfo に正解アイテムが無いため、結果画面用の値は
    //      GetSecretItemId(PlayerId) から読んでください。
    //      ゲーム終了後も保持されます（ResetForNewRound で消えます）。
    //
    //  (B) OnTopicPresented の第2引数は topicId として使っています。
    //      お題は盤面スロットとは無関係な独立データのためです。
    //      お題テキストは UI 側が topicId から引いてください。
    //
    //  (C) お題が複数出る場合（ミスのペナルティ）は、
    //      OnTopicPresented を連続で複数回発火します。
    //      「1個ずつ送って表示」は送り用の入力メソッドが必要なため、
    //      現時点ではまとめて通知する形にしています。
    //
    //  (D) お題の種類数は TopicCount プロパティで設定してください。
    //      StartGame より前に設定します（既定 10）。
    //
    //  (E) 画面遷移の判断材料として CurrentPhase を追加しました。
    //      OnTurnChanged は「手番が変わったとき」に加えて
    //      「同じ人のまま次の操作へ進んだとき」にも発火します。
    //      UI は OnTurnChanged を受けて CurrentPhase を見てください。
    //
    //  (F) 乱数はお題の抽選にのみ使います。ルール層を両クライアントで
    //      動かす場合は、StartGame の前に RandomSeed へ同じ値を
    //      設定してください（マスターのみで動かすなら不要）。
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
        /// <summary>いま外部に求めている操作。UI の画面切り替え用。</summary>
        public enum Phase
        {
            /// <summary>未開始 / 終了後</summary>
            Idle = 0,

            /// <summary>秘密のアイテムを選ぶ</summary>
            SecretSelection = 1,

            /// <summary>解答するかどうかを選ぶ</summary>
            AnswerDecision = 2,

            /// <summary>盤面からアイテムを選ぶ</summary>
            ItemSelection = 3,

            /// <summary>確認ダイアログ</summary>
            Confirmation = 4,

            /// <summary>結果表示</summary>
            Finished = 5
        }

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

        /// <summary>現在の手番。いま入力を待っているプレイヤーを指します。</summary>
        public PlayerId CurrentTurn { get; private set; }

        /// <summary>ゲームが進行中かどうか。</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>いま外部に求めている操作。</summary>
        public Phase CurrentPhase { get; private set; }

        /// <summary>現在のターン数（1 始まり）。</summary>
        public int TurnNumber { get; private set; }

        /// <summary>盤面のスロット数。</summary>
        public int SlotCount
        {
            get { return _itemIds == null ? 0 : _itemIds.Length; }
        }

        // ---------------------------------------------------------
        //  外部から設定する値（StartGame より前に設定）
        // ---------------------------------------------------------

        /// <summary>お題の種類数。topicId は 0 〜 TopicCount-1 の範囲で発番されます。</summary>
        public int TopicCount { get; set; }

        /// <summary>お題抽選の乱数シード。0 以下なら実行時刻から自動生成します。</summary>
        public int RandomSeed { get; set; }

        /// <summary>1 回のヒントで出すお題数の上限。0 以下で無制限。</summary>
        public int MaxTopicsPerHint { get; set; }

        /// <summary>この数のターンを終えても決着しなければ引き分け。</summary>
        public int MaxTurnCount { get; set; }

        // ---------------------------------------------------------
        //  内部状態
        // ---------------------------------------------------------

        private int[] _itemIds;
        private PlayerId _firstPlayer = PlayerId.None;

        private readonly int[] _secretItemIds = { -1, -1 };
        private readonly int[] _secretSlotIndices = { -1, -1 };

        /// <summary>未消化のペナルティ。出題側へ回った時点で 0 に戻ります。</summary>
        private readonly int[] _pendingPenalties = new int[2];

        private readonly bool[] _correctThisTurn = new bool[2];

        private readonly List<int>[] _topicDecks = { new List<int>(), new List<int>() };
        private readonly int[] _topicCursors = new int[2];

        private Random _random;

        private int _halfIndex;
        private PlayerId _hintGiver = PlayerId.None;
        private PlayerId _answerer = PlayerId.None;
        private int _pendingSlotIndex = -1;

        public GameController()
        {
            TopicCount = 10;
            MaxTopicsPerHint = 5;
            MaxTurnCount = 999;
            CurrentPhase = Phase.Idle;
            CurrentTurn = PlayerId.None;
        }

        // ---------------------------------------------------------
        //  読み取り用の補助 API
        // ---------------------------------------------------------

        /// <summary>スロット番号からアイテム ID を取得します。範囲外は -1。</summary>
        public int GetItemIdAt(int slotIndex)
        {
            if (_itemIds == null || slotIndex < 0 || slotIndex >= _itemIds.Length) return -1;
            return _itemIds[slotIndex];
        }

        /// <summary>
        /// 指定プレイヤーの秘密のアイテム ID。未選択なら -1。
        /// GameFinishedInfo に含められないため、結果画面はここから読んでください。
        /// </summary>
        public int GetSecretItemId(PlayerId player)
        {
            var index = IndexOf(player);
            return index < 0 ? -1 : _secretItemIds[index];
        }

        /// <summary>指定プレイヤーの秘密のアイテムがあるスロット番号。未選択なら -1。</summary>
        public int GetSecretSlotIndex(PlayerId player)
        {
            var index = IndexOf(player);
            return index < 0 ? -1 : _secretSlotIndices[index];
        }

        /// <summary>未消化のペナルティ数。</summary>
        public int GetPendingPenalty(PlayerId player)
        {
            var index = IndexOf(player);
            return index < 0 ? 0 : _pendingPenalties[index];
        }

        /// <summary>次にこのプレイヤーが出題側になったとき提示されるお題の数。</summary>
        public int GetTopicCount(PlayerId player)
        {
            var index = IndexOf(player);
            if (index < 0) return 0;

            var count = 1 + _pendingPenalties[index];
            if (MaxTopicsPerHint > 0 && count > MaxTopicsPerHint) count = MaxTopicsPerHint;
            return count;
        }

        /// <summary>このハーフターンで出題している側。</summary>
        public PlayerId CurrentHintGiver
        {
            get { return _hintGiver; }
        }

        /// <summary>このハーフターンで解答する側。</summary>
        public PlayerId CurrentAnswerer
        {
            get { return _answerer; }
        }

        /// <summary>確認ダイアログで保留中のスロット番号。無ければ -1。</summary>
        public int PendingSlotIndex
        {
            get { return _pendingSlotIndex; }
        }

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
            if (itemIds == null || itemIds.Length == 0)
            {
                Log?.Invoke("StartGame: itemIds が空です。");
                return;
            }

            if (firstPlayer == PlayerId.None)
            {
                Log?.Invoke("StartGame: firstPlayer が None です。");
                return;
            }

            if (TopicCount <= 0)
            {
                Log?.Invoke("StartGame: TopicCount が 0 以下です。");
                return;
            }

            ClearState();

            _itemIds = new int[itemIds.Length];
            Array.Copy(itemIds, _itemIds, itemIds.Length);

            _firstPlayer = firstPlayer;
            _random = new Random(RandomSeed > 0 ? RandomSeed : Environment.TickCount);

            RefillTopicDeck(PlayerId.Player1);
            RefillTopicDeck(PlayerId.Player2);

            SetPlaying(true);
            TurnNumber = 0;

            CurrentPhase = Phase.SecretSelection;
            SetTurn(_firstPlayer);
            Log?.Invoke("ゲーム開始。先手 " + _firstPlayer + " が秘密のアイテムを選びます。");
        }

        /// <summary>
        /// 相手が退出したため、ゲームを中断します。
        /// 待機中・ローディング中・対局中のどのタイミングでも呼ばれ得ます。
        /// 既に終了している場合は何もせずに return してください。
        /// </summary>
        /// <param name="leaver">退出したプレイヤー</param>
        public void AbortGame(PlayerId leaver)
        {
            if (!IsPlaying)
            {
                Log?.Invoke("AbortGame: 進行中ではないため無視します。");
                return;
            }

            Log?.Invoke("AbortGame: " + leaver + " が退出しました。");
            FinishGame(PlayerId.None, GameEndReason.OpponentLeft);
        }

        /// <summary>
        /// 再戦のために状態を初期化します。
        /// シーンは再読み込みせず、このメソッドで初期状態に戻します。
        /// そのため、持ち越してはいけない状態が残らないよう注意してください。
        /// </summary>
        public void ResetForNewRound()
        {
            ClearState();
            SetPlaying(false);
            CurrentPhase = Phase.Idle;
            CurrentTurn = PlayerId.None;
            Log?.Invoke("状態を初期化しました。");
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
            if (!Expect(Phase.SecretSelection, player)) return;
            if (!ValidateSlot(slotIndex)) return;

            var index = IndexOf(player);
            _secretSlotIndices[index] = slotIndex;
            _secretItemIds[index] = _itemIds[slotIndex];

            Log?.Invoke(player + " が秘密のアイテムを決定しました。");

            if (player == _firstPlayer)
            {
                SetTurn(Opponent(_firstPlayer));
                return;
            }

            BeginTurn();
        }

        /// <summary>
        /// 出題に答えるかどうかの意思表示。
        /// </summary>
        public void SubmitAnswerDecision(PlayerId player, bool willAnswer)
        {
            if (!Expect(Phase.AnswerDecision, player)) return;

            if (!willAnswer)
            {
                Log?.Invoke(player + " は解答を見送りました。");
                EndHalfTurn();
                return;
            }

            CurrentPhase = Phase.ItemSelection;
            SetTurn(player);
        }

        /// <summary>
        /// 盤面からアイテムを 1 つ選びます。
        /// 判定結果は OnJudged で通知してください。
        /// </summary>
        public void SubmitItemSelection(PlayerId player, int slotIndex)
        {
            if (!Expect(Phase.ItemSelection, player)) return;
            if (!ValidateSlot(slotIndex)) return;

            // 仕様に確認ダイアログがあるため、ここでは判定せず確認待ちにします。
            // 判定は SubmitConfirmation(true) の中で行い、OnJudged で通知します。
            _pendingSlotIndex = slotIndex;
            CurrentPhase = Phase.Confirmation;
            SetTurn(player);
        }

        /// <summary>
        /// 確認ダイアログなどの最終確定。
        /// </summary>
        public void SubmitConfirmation(PlayerId player, bool confirmed)
        {
            if (!Expect(Phase.Confirmation, player)) return;

            if (!confirmed)
            {
                // キャンセル。ペナルティは付きません。
                _pendingSlotIndex = -1;
                CurrentPhase = Phase.ItemSelection;
                SetTurn(player);
                return;
            }

            var index = IndexOf(player);
            var slotIndex = _pendingSlotIndex;
            var guessedItemId = GetItemIdAt(slotIndex);
            var targetItemId = _secretItemIds[IndexOf(Opponent(player))];
            var correct = guessedItemId >= 0 && guessedItemId == targetItemId;

            if (correct)
            {
                _correctThisTurn[index] = true;
            }
            else
            {
                _pendingPenalties[index]++;
            }

            _pendingSlotIndex = -1;

            var report = new JudgeReport
            {
                Answerer = player,
                SlotIndex = slotIndex,
                ItemId = guessedItemId,
                Correct = correct
            };

            Log?.Invoke(player + " の解答: itemId=" + guessedItemId + " → " + (correct ? "正解" : "不正解"));
            RaiseJudged(report);

            // ★ここで勝敗を確定させないこと。
            //   前半で正解しても後半の相手に解答機会が残るため
            //   （両者正解なら引き分け）、判定はターン末まで保留します。
            EndHalfTurn();
        }

        // ---------------------------------------------------------
        //  進行
        // ---------------------------------------------------------

        private void BeginTurn()
        {
            TurnNumber++;
            _correctThisTurn[0] = false;
            _correctThisTurn[1] = false;
            BeginHalfTurn(0);
        }

        private void BeginHalfTurn(int halfIndex)
        {
            _halfIndex = halfIndex;
            _hintGiver = halfIndex == 0 ? _firstPlayer : Opponent(_firstPlayer);
            _answerer = Opponent(_hintGiver);

            var giverIndex = IndexOf(_hintGiver);
            var topicNum = GetTopicCount(_hintGiver);

            Log?.Invoke("--- " + TurnNumber + "ターン目 " + (halfIndex == 0 ? "前半" : "後半")
                        + "（出題 " + _hintGiver + " → 解答 " + _answerer + "）お題 " + topicNum + " 個");

            for (var i = 0; i < topicNum; i++)
            {
                RaiseTopicPresented(_hintGiver, DrawTopic(_hintGiver));
            }

            // ★ペナルティはここで消化。次回のヒントはまた 1 個に戻ります。
            _pendingPenalties[giverIndex] = 0;

            CurrentPhase = Phase.AnswerDecision;
            SetTurn(_answerer);
        }

        private void EndHalfTurn()
        {
            if (_halfIndex == 0)
            {
                BeginHalfTurn(1);
                return;
            }

            // 両ハーフターンが終わったのでここで初めて勝敗を確定します。
            var p1 = _correctThisTurn[0];
            var p2 = _correctThisTurn[1];

            if (p1 && p2)
            {
                FinishGame(PlayerId.None, GameEndReason.Draw);
                return;
            }

            if (p1)
            {
                FinishGame(PlayerId.Player1, GameEndReason.Normal);
                return;
            }

            if (p2)
            {
                FinishGame(PlayerId.Player2, GameEndReason.Normal);
                return;
            }

            if (MaxTurnCount > 0 && TurnNumber >= MaxTurnCount)
            {
                FinishGame(PlayerId.None, GameEndReason.Draw);
                return;
            }

            BeginTurn();
        }

        private void FinishGame(PlayerId winner, GameEndReason reason)
        {
            SetPlaying(false);
            CurrentPhase = Phase.Finished;
            _pendingSlotIndex = -1;

            var info = new GameFinishedInfo
            {
                Winner = winner,
                Reason = reason
            };

            Log?.Invoke("ゲーム終了: winner=" + winner + " reason=" + reason
                        + " / Player1の正解=" + _secretItemIds[0]
                        + " Player2の正解=" + _secretItemIds[1]);

            RaiseGameFinished(info);
        }

        // ---------------------------------------------------------
        //  お題の抽選
        // ---------------------------------------------------------

        private int DrawTopic(PlayerId player)
        {
            var index = IndexOf(player);
            if (index < 0) return 0;

            if (_topicCursors[index] >= _topicDecks[index].Count)
            {
                RefillTopicDeck(player); // 枯渇したら山札をリセット
            }

            var topicId = _topicDecks[index][_topicCursors[index]];
            _topicCursors[index]++;
            return topicId;
        }

        private void RefillTopicDeck(PlayerId player)
        {
            var index = IndexOf(player);
            if (index < 0) return;

            var ids = new int[TopicCount];
            for (var i = 0; i < TopicCount; i++) ids[i] = i;
            Shuffle(ids);

            _topicDecks[index].Clear();
            _topicDecks[index].AddRange(ids);
            _topicCursors[index] = 0;
        }

        private void Shuffle(int[] values)
        {
            if (_random == null) _random = new Random(Environment.TickCount);

            for (var i = values.Length - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                var tmp = values[i];
                values[i] = values[j];
                values[j] = tmp;
            }
        }

        // ---------------------------------------------------------
        //  補助
        // ---------------------------------------------------------

        private void ClearState()
        {
            _itemIds = null;
            _firstPlayer = PlayerId.None;
            _hintGiver = PlayerId.None;
            _answerer = PlayerId.None;
            _halfIndex = 0;
            _pendingSlotIndex = -1;
            TurnNumber = 0;

            for (var i = 0; i < 2; i++)
            {
                _secretItemIds[i] = -1;
                _secretSlotIndices[i] = -1;
                _pendingPenalties[i] = 0;
                _correctThisTurn[i] = false;
                _topicDecks[i].Clear();
                _topicCursors[i] = 0;
            }
        }

        private static PlayerId Opponent(PlayerId player)
        {
            if (player == PlayerId.Player1) return PlayerId.Player2;
            if (player == PlayerId.Player2) return PlayerId.Player1;
            return PlayerId.None;
        }

        private static int IndexOf(PlayerId player)
        {
            if (player == PlayerId.Player1) return 0;
            if (player == PlayerId.Player2) return 1;
            return -1;
        }

        /// <summary>フェーズと手番が一致しているかを確認します。不一致なら false。</summary>
        private bool Expect(Phase phase, PlayerId player)
        {
            if (!IsPlaying)
            {
                Log?.Invoke("入力を無視: ゲームが進行中ではありません。");
                return false;
            }

            if (CurrentPhase != phase)
            {
                Log?.Invoke("入力を無視: phase=" + CurrentPhase + " expected=" + phase);
                return false;
            }

            if (player != CurrentTurn)
            {
                Log?.Invoke("入力を無視: player=" + player + " expected=" + CurrentTurn);
                return false;
            }

            return true;
        }

        private bool ValidateSlot(int slotIndex)
        {
            if (_itemIds != null && slotIndex >= 0 && slotIndex < _itemIds.Length) return true;
            Log?.Invoke("入力を無視: 不正なスロット " + slotIndex);
            return false;
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
            var board = new int[24];
            for (var i = 0; i < board.Length; i++) board[i] = i;

            // --- ケース1: 前半に正解 → 後半は不正解 → 先に解答した側の勝ち
            RunScenario("ケース1: 片方だけ正解", board, true, false);

            // --- ケース2: 両者正解 → 引き分け（前半で終了しないことの確認）
            RunScenario("ケース2: 両者正解", board, true, true);

            // --- ケース3: 両者不正解 → 決着せず次ターンへ
            var c3 = NewControllerForTest();
            c3.StartGame(board, PlayerId.Player1);
            c3.SubmitSecretItem(PlayerId.Player1, 3);
            c3.SubmitSecretItem(PlayerId.Player2, 7);
            AnswerOnce(c3, PlayerId.Player2, 0);  // 外す
            AnswerOnce(c3, PlayerId.Player1, 1);  // 外す
            Log?.Invoke("ケース3: 未決着で続行 → TurnNumber=" + c3.TurnNumber
                        + " (2 なら正しい) / IsPlaying=" + c3.IsPlaying);
            Log?.Invoke("ケース3: ペナルティ消化前 Player2の次回お題数="
                        + c3.GetTopicCount(PlayerId.Player2) + " (1 なら消化済み)");
        }

        private void RunScenario(string label, int[] board, bool firstHalfCorrect, bool secondHalfCorrect)
        {
            var c = NewControllerForTest();

            var winner = PlayerId.None;
            var reason = GameEndReason.Normal;
            var finishedCount = 0;
            c.OnGameFinished += info =>
            {
                winner = info.Winner;
                reason = info.Reason;
                finishedCount++;
            };

            c.StartGame(board, PlayerId.Player1);
            c.SubmitSecretItem(PlayerId.Player1, 3);   // Player1 の正解は itemId 3
            c.SubmitSecretItem(PlayerId.Player2, 7);   // Player2 の正解は itemId 7

            // 前半: 出題 Player1 → 解答 Player2。Player1 の正解は slot 3。
            AnswerOnce(c, PlayerId.Player2, firstHalfCorrect ? 3 : 0);

            // 前半で正解していてもここで終わっていないことが重要。
            Log?.Invoke(label + ": 前半終了時点の finished 回数=" + finishedCount + " (0 なら正しい)");

            // 後半: 出題 Player2 → 解答 Player1。Player2 の正解は slot 7。
            AnswerOnce(c, PlayerId.Player1, secondHalfCorrect ? 7 : 0);

            Log?.Invoke(label + ": winner=" + winner + " reason=" + reason
                        + " finished回数=" + finishedCount);
        }

        private GameController NewControllerForTest()
        {
            var c = new GameController();
            c.TopicCount = TopicCount;
            c.RandomSeed = 12345;
            c.Log = Log;
            return c;
        }

        private static void AnswerOnce(GameController c, PlayerId player, int slotIndex)
        {
            c.SubmitAnswerDecision(player, true);
            c.SubmitItemSelection(player, slotIndex);
            c.SubmitConfirmation(player, true);
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
