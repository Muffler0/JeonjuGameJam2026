using System;
using System.Collections.Generic;

namespace Project.Gameplay
{
    // =============================================================
    //  Toy Tangle ルール層
    //
    //  ・UnityEngine 不使用。純粋な C# のみ。
    //  ・例外は投げず、不正な入力は Log に理由を出して無視します。
    //  ・static 状態なし。ResetForNewRound で完全に初期化されます。
    //
    //  ターン構造（企画書の用語と 1:1）
    //    1 ターン = 一人の推測。相手はそのターンにお題を受け取って説明します。
    //    奇数ターンは先手、偶数ターンは後手が推測します。
    //    10 ターンが終わると「追加ターン」で両者が同時に推測します。
    //
    //  ペナルティトークン
    //    推測を外すと 1 個獲得（最大 1 個）。保持中は推測できません。
    //    自分が説明する番になってお題を引く時点で消滅し、
    //    このときお題を 2 個引いて説明することになります。
    //    最後の 10 ターン目で得たトークンは引く機会がないため追加ターンまで残り、
    //    両者正解のときに敗北条件として働きます。
    // =============================================================

    public class GameController
    {
        /// <summary>いま外部に求めている操作。UI の画面切り替え用。</summary>
        public enum Phase
        {
            /// <summary>未開始 / 終了後</summary>
            Idle = 0,

            /// <summary>おもちゃの選択（両者同時）</summary>
            SecretSelection = 1,

            /// <summary>推測するか見送るかの選択（推測側のみ）</summary>
            AnswerDecision = 2,

            /// <summary>盤面からおもちゃを選ぶ（推測側のみ）</summary>
            ItemSelection = 3,

            /// <summary>確認ダイアログ（推測側のみ）</summary>
            Confirmation = 4,

            /// <summary>追加ターン。両者が同時に選択し確定します。</summary>
            FinalGuess = 5,

            /// <summary>結果表示</summary>
            Finished = 6
        }

        // -------------------------------------------------------
        //  通知イベント
        // -------------------------------------------------------

        /// <summary>お題が提示された。(説明する側, topicId)</summary>
        public event Action<PlayerId, int> OnTopicPresented;

        /// <summary>判定が 1 回行われた。追加ターンでは 2 回発生します。</summary>
        public event Action<JudgeReport> OnJudged;

        /// <summary>ゲームが終了した。画面遷移はこのイベントでのみ行ってください。</summary>
        public event Action<GameFinishedInfo> OnGameFinished;

        /// <summary>入力待ちの相手が変わった。同時進行の区間では PlayerId.None が来ます。</summary>
        public event Action<PlayerId> OnTurnChanged;

        /// <summary>ターンが始まった。(ターン番号, 最大ターン数, 追加ターンか)</summary>
        public event Action<int, int, bool> OnTurnStarted;

        /// <summary>ペナルティトークンの保持状態が変わった。(対象, 保持しているか)</summary>
        public event Action<PlayerId, bool> OnPenaltyChanged;

        /// <summary>デバッグ用のログ出力口。Unity 側が受け取って表示します。</summary>
        public Action<string> Log;

        // -------------------------------------------------------
        //  読み取り専用の状態
        // -------------------------------------------------------

        /// <summary>いま入力を待っているプレイヤー。同時進行の区間では None。</summary>
        public PlayerId CurrentTurn { get; private set; }

        public bool IsPlaying { get; private set; }

        public Phase CurrentPhase { get; private set; }

        /// <summary>現在のターン番号（1 始まり）。追加ターンは MaxTurnCount + 1。</summary>
        public int TurnNumber { get; private set; }

        /// <summary>いまが追加ターン（同時推測）か。</summary>
        public bool IsExtraTurn { get { return CurrentPhase == Phase.FinalGuess; } }

        /// <summary>このターンで説明する側。</summary>
        public PlayerId CurrentDescriber { get; private set; }

        /// <summary>盤面のスロット数。</summary>
        public int SlotCount { get { return _itemIds == null ? 0 : _itemIds.Length; } }

        // -------------------------------------------------------
        //  StartGame より前に設定する値
        // -------------------------------------------------------

        /// <summary>お題プールの大きさ。10 ターン + ペナルティ分を賄うには 15 以上を推奨。</summary>
        public int TopicCount { get; set; }

        /// <summary>お題抽選の乱数シード。0 以下なら実行時刻から自動生成します。</summary>
        public int RandomSeed { get; set; }

        /// <summary>総ターン数。企画書の基準では 10。</summary>
        public int MaxTurnCount { get; set; }

        // -------------------------------------------------------
        //  内部状態
        // -------------------------------------------------------

        private int[] _itemIds;
        private PlayerId _firstPlayer = PlayerId.None;

        private readonly int[] _secretItemIds = { -1, -1 };
        private readonly int[] _secretSlotIndices = { -1, -1 };

        /// <summary>ペナルティトークンの保持。最大 1 個なので bool で十分です。</summary>
        private readonly bool[] _hasPenalty = new bool[2];

        /// <summary>確認待ちのスロット。追加ターンでは両者が各自持ちます。</summary>
        private readonly int[] _pendingSlots = { -1, -1 };

        /// <summary>追加ターンで確定提出を終えたか。</summary>
        private readonly bool[] _finalSubmitted = new bool[2];

        /// <summary>共用のお題デッキ。1 試合で引いたお題は二度と出ません。</summary>
        private readonly List<int> _topicDeck = new List<int>();
        private int _topicCursor;

        private Random _random;

        public GameController()
        {
            TopicCount = 15;
            MaxTurnCount = 10;
            CurrentPhase = Phase.Idle;
            CurrentTurn = PlayerId.None;
            CurrentDescriber = PlayerId.None;
        }

        // -------------------------------------------------------
        //  読み取り補助 API
        // -------------------------------------------------------

        /// <summary>スロットのアイテム ID。範囲外は -1。</summary>
        public int GetItemIdAt(int slotIndex)
        {
            if (_itemIds == null || slotIndex < 0 || slotIndex >= _itemIds.Length) return -1;
            return _itemIds[slotIndex];
        }

        /// <summary>指定プレイヤーの正解アイテム ID。未選択なら -1。</summary>
        public int GetSecretItemId(PlayerId player)
        {
            var i = IndexOf(player);
            return i < 0 ? -1 : _secretItemIds[i];
        }

        /// <summary>指定プレイヤーの正解があるスロット番号。未選択なら -1。</summary>
        public int GetSecretSlotIndex(PlayerId player)
        {
            var i = IndexOf(player);
            return i < 0 ? -1 : _secretSlotIndices[i];
        }

        /// <summary>ペナルティトークンを持っているか。</summary>
        public bool HasPenalty(PlayerId player)
        {
            var i = IndexOf(player);
            return i >= 0 && _hasPenalty[i];
        }

        /// <summary>おもちゃの選択を終えたか。</summary>
        public bool HasChosenSecret(PlayerId player)
        {
            var i = IndexOf(player);
            return i >= 0 && _secretSlotIndices[i] >= 0;
        }

        /// <summary>追加ターンで提出を終えたか。</summary>
        public bool HasSubmittedFinalGuess(PlayerId player)
        {
            var i = IndexOf(player);
            return i >= 0 && _finalSubmitted[i];
        }

        /// <summary>いま推測できるか。トークン保持中は不可（追加ターンは例外）。</summary>
        public bool CanGuess(PlayerId player)
        {
            if (IsExtraTurn) return true;
            return !HasPenalty(player);
        }

        /// <summary>確認待ちのスロット。無ければ -1。</summary>
        public int GetPendingSlot(PlayerId player)
        {
            var i = IndexOf(player);
            return i < 0 ? -1 : _pendingSlots[i];
        }

        /// <summary>次にこのプレイヤーが説明するとき提示されるお題の数。</summary>
        public int GetTopicCountFor(PlayerId player)
        {
            return HasPenalty(player) ? 2 : 1;
        }

        // -------------------------------------------------------
        //  開始 / 終了
        // -------------------------------------------------------

        /// <summary>
        /// ゲームを開始します。盤面配列はマスターが作ったものをそのまま受け取ります。
        /// </summary>
        /// <param name="itemIds">スロット順に並んだアイテム ID</param>
        /// <param name="firstPlayer">先手（奇数ターンに推測する側）</param>
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

            // 10 ターン + ペナルティ分を重複なしで引く必要があるため余裕が要ります。
            if (TopicCount < MaxTurnCount)
            {
                Log?.Invoke($"StartGame: TopicCount({TopicCount}) が MaxTurnCount({MaxTurnCount}) より小さいです。" +
                            "お題が足りない場合はデッキを引き直します。");
            }

            ClearState();

            _itemIds = new int[itemIds.Length];
            Array.Copy(itemIds, _itemIds, itemIds.Length);

            _firstPlayer = firstPlayer;
            _random = new Random(RandomSeed > 0 ? RandomSeed : Environment.TickCount);

            RefillTopicDeck();

            IsPlaying = true;
            TurnNumber = 0;

            // おもちゃの選択は両者同時です。特定プレイヤーの手番ではありません。
            CurrentPhase = Phase.SecretSelection;
            SetTurn(PlayerId.None);

            Log?.Invoke($"ゲーム開始。先手 {_firstPlayer}。両者がおもちゃを選びます。");
        }

        /// <summary>相手の退出により中断します。既に終了していれば無視します。</summary>
        public void AbortGame(PlayerId leaver)
        {
            if (!IsPlaying)
            {
                Log?.Invoke("AbortGame: 進行中ではないため無視します。");
                return;
            }

            Log?.Invoke($"AbortGame: {leaver} が退出しました。");
            FinishGame(PlayerId.None, GameEndReason.OpponentLeft);
        }

        /// <summary>再戦のために状態を完全に初期化します。</summary>
        public void ResetForNewRound()
        {
            ClearState();
            IsPlaying = false;
            CurrentPhase = Phase.Idle;
            SetTurn(PlayerId.None);
            Log?.Invoke("状態を初期化しました。");
        }

        // -------------------------------------------------------
        //  入力
        // -------------------------------------------------------

        /// <summary>
        /// 自分のおもちゃを決めます。両者が決めた時点で 1 ターン目が始まります。
        /// UI の 10 秒タイマーが切れたら、任意のスロットでこのメソッドを呼んでください。
        /// </summary>
        public void SubmitSecretItem(PlayerId player, int slotIndex)
        {
            if (!IsPlaying)
            {
                Log?.Invoke("入力を無視: 進行中ではありません。");
                return;
            }

            if (CurrentPhase != Phase.SecretSelection)
            {
                Log?.Invoke($"入力を無視: phase={CurrentPhase}（SecretSelection ではない）");
                return;
            }

            var index = IndexOf(player);
            if (index < 0 || !ValidateSlot(slotIndex)) return;

            if (_secretSlotIndices[index] >= 0)
            {
                Log?.Invoke($"入力を無視: {player} は既におもちゃを選んでいます。");
                return;
            }

            _secretSlotIndices[index] = slotIndex;
            _secretItemIds[index] = _itemIds[slotIndex];

            Log?.Invoke($"{player} がおもちゃを選びました。");

            // 両者が選び終わったときだけ進行します。
            if (HasChosenSecret(PlayerId.Player1) && HasChosenSecret(PlayerId.Player2))
            {
                BeginTurn(1);
            }
        }

        /// <summary>
        /// 推測するか(true)見送るか(false)を決めます。
        /// false は企画書の "enough"（OK ボタン）に相当し、そのターンを終わらせます。
        /// </summary>
        public void SubmitAnswerDecision(PlayerId player, bool willAnswer)
        {
            if (!Expect(Phase.AnswerDecision, player)) return;

            if (!willAnswer)
            {
                Log?.Invoke($"{player} が推測を見送りました。");
                EndTurn();
                return;
            }

            if (!CanGuess(player))
            {
                Log?.Invoke($"入力を無視: {player} はペナルティトークン保持中のため推測できません。");
                return;
            }

            CurrentPhase = Phase.ItemSelection;
            SetTurn(player);
        }

        /// <summary>
        /// 盤面からおもちゃを 1 つ選びます。判定は確認のあとで行われます。
        /// 追加ターンでは両者が各自呼び出せます。
        /// </summary>
        public void SubmitItemSelection(PlayerId player, int slotIndex)
        {
            var index = IndexOf(player);
            if (index < 0 || !ValidateSlot(slotIndex)) return;

            if (IsExtraTurn)
            {
                if (_finalSubmitted[index])
                {
                    Log?.Invoke($"入力を無視: {player} は既に最終推測を確定しています。");
                    return;
                }

                _pendingSlots[index] = slotIndex;
                Log?.Invoke($"{player} が最終候補としてスロット {slotIndex} を選びました。");
                return;
            }

            if (!Expect(Phase.ItemSelection, player)) return;

            _pendingSlots[index] = slotIndex;
            CurrentPhase = Phase.Confirmation;
            SetTurn(player);
        }

        /// <summary>
        /// 選択を確定またはキャンセルします。
        /// 追加ターンでは確定後に相手を待ち、両者が揃った時点でまとめて判定します。
        /// </summary>
        public void SubmitConfirmation(PlayerId player, bool confirmed)
        {
            var index = IndexOf(player);
            if (index < 0) return;

            if (IsExtraTurn)
            {
                HandleFinalConfirmation(index, player, confirmed);
                return;
            }

            if (!Expect(Phase.Confirmation, player)) return;

            if (!confirmed)
            {
                // キャンセルにペナルティはありません。選び直せます。
                _pendingSlots[index] = -1;
                CurrentPhase = Phase.ItemSelection;
                SetTurn(player);
                return;
            }

            var slotIndex = _pendingSlots[index];
            _pendingSlots[index] = -1;

            var correct = Judge(player, slotIndex);

            if (correct)
            {
                // 先に当てた側の勝ち。ここで即座に終了します。
                FinishGame(player, GameEndReason.Normal);
                return;
            }

            SetPenalty(player, true);
            EndTurn();
        }

        private void HandleFinalConfirmation(int index, PlayerId player, bool confirmed)
        {
            if (_finalSubmitted[index])
            {
                Log?.Invoke($"入力を無視: {player} は既に確定しています。");
                return;
            }

            if (!confirmed)
            {
                _pendingSlots[index] = -1;
                return;
            }

            if (_pendingSlots[index] < 0)
            {
                Log?.Invoke($"入力を無視: {player} が選んだスロットがありません。");
                return;
            }

            _finalSubmitted[index] = true;
            Log?.Invoke($"{player} が最終推測を確定しました。");

            if (_finalSubmitted[0] && _finalSubmitted[1])
            {
                ResolveExtraTurn();
            }
        }

        // -------------------------------------------------------
        //  進行
        // -------------------------------------------------------

        private void BeginTurn(int turnNumber)
        {
            TurnNumber = turnNumber;

            // 奇数ターンは先手が推測します。
            var guesser = (turnNumber % 2 == 1) ? _firstPlayer : Opponent(_firstPlayer);
            var describer = Opponent(guesser);
            CurrentDescriber = describer;

            OnTurnStarted?.Invoke(TurnNumber, MaxTurnCount, false);

            // 説明する側がトークンを持っていればお題をもう 1 つ引き、ここで消滅します。
            var topicNum = GetTopicCountFor(describer);

            Log?.Invoke($"--- {turnNumber} ターン目（説明 {describer} → 推測 {guesser}）お題 {topicNum} 個");

            for (var i = 0; i < topicNum; i++)
            {
                OnTopicPresented?.Invoke(describer, DrawTopic());
            }

            SetPenalty(describer, false);

            CurrentPhase = Phase.AnswerDecision;
            SetTurn(guesser);
        }

        private void EndTurn()
        {
            if (TurnNumber < MaxTurnCount)
            {
                BeginTurn(TurnNumber + 1);
                return;
            }

            BeginExtraTurn();
        }

        /// <summary>10 ターンがすべて終わったあとの同時推測ターン。</summary>
        private void BeginExtraTurn()
        {
            TurnNumber = MaxTurnCount + 1;
            CurrentDescriber = PlayerId.None;

            _pendingSlots[0] = -1;
            _pendingSlots[1] = -1;
            _finalSubmitted[0] = false;
            _finalSubmitted[1] = false;

            Log?.Invoke("--- 追加ターン。両者が同時に最終推測を行います。");

            CurrentPhase = Phase.FinalGuess;
            OnTurnStarted?.Invoke(TurnNumber, MaxTurnCount, true);

            SetTurn(PlayerId.None);
        }

        private void ResolveExtraTurn()
        {
            var p1Correct = Judge(PlayerId.Player1, _pendingSlots[0]);
            var p2Correct = Judge(PlayerId.Player2, _pendingSlots[1]);

            // 両者とも外れならトークンに関係なく引き分け。
            if (!p1Correct && !p2Correct)
            {
                FinishGame(PlayerId.None, GameEndReason.Draw);
                return;
            }

            // 片方だけ正解ならその側の勝ち。
            if (p1Correct != p2Correct)
            {
                FinishGame(p1Correct ? PlayerId.Player1 : PlayerId.Player2, GameEndReason.Normal);
                return;
            }

            // 両者正解の場合。トークンを持つ側が敗北し、状態が同じなら引き分け。
            if (_hasPenalty[0] == _hasPenalty[1])
            {
                FinishGame(PlayerId.None, GameEndReason.Draw);
                return;
            }

            FinishGame(_hasPenalty[0] ? PlayerId.Player2 : PlayerId.Player1, GameEndReason.Normal);
        }

        /// <summary>判定して OnJudged を発火します。戻り値は正解かどうか。</summary>
        private bool Judge(PlayerId player, int slotIndex)
        {
            var guessedItemId = GetItemIdAt(slotIndex);
            var targetItemId = GetSecretItemId(Opponent(player));
            var correct = guessedItemId >= 0 && guessedItemId == targetItemId;

            Log?.Invoke($"{player} の推測: itemId={guessedItemId} → {(correct ? "正解" : "不正解")}");

            OnJudged?.Invoke(new JudgeReport
            {
                Answerer = player,
                SlotIndex = slotIndex,
                ItemId = guessedItemId,
                Correct = correct
            });

            return correct;
        }

        private void FinishGame(PlayerId winner, GameEndReason reason)
        {
            IsPlaying = false;
            CurrentPhase = Phase.Finished;
            CurrentDescriber = PlayerId.None;
            _pendingSlots[0] = -1;
            _pendingSlots[1] = -1;

            Log?.Invoke($"ゲーム終了: winner={winner} reason={reason} / " +
                        $"Player1 の正解={_secretItemIds[0]} Player2 の正解={_secretItemIds[1]}");

            SetTurn(PlayerId.None);

            OnGameFinished?.Invoke(new GameFinishedInfo
            {
                Winner = winner,
                Reason = reason
            });
        }

        // -------------------------------------------------------
        //  お題の抽選（共用デッキ・重複なし）
        // -------------------------------------------------------

        private int DrawTopic()
        {
            if (_topicCursor >= _topicDeck.Count)
            {
                // プールが足りない場合は引き直します。本来はプールを増やすのが正しい対処です。
                Log?.Invoke("お題デッキが尽きたため引き直します。TopicCount を増やすことを推奨します。");
                RefillTopicDeck();
            }

            return _topicDeck[_topicCursor++];
        }

        private void RefillTopicDeck()
        {
            var ids = new int[TopicCount];
            for (var i = 0; i < TopicCount; i++) ids[i] = i;
            Shuffle(ids);

            _topicDeck.Clear();
            _topicDeck.AddRange(ids);
            _topicCursor = 0;
        }

        private void Shuffle(int[] values)
        {
            if (_random == null) _random = new Random(Environment.TickCount);

            for (var i = values.Length - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        // -------------------------------------------------------
        //  補助
        // -------------------------------------------------------

        private void SetTurn(PlayerId player)
        {
            CurrentTurn = player;
            OnTurnChanged?.Invoke(player);
        }

        private void SetPenalty(PlayerId player, bool value)
        {
            var i = IndexOf(player);
            if (i < 0 || _hasPenalty[i] == value) return;

            _hasPenalty[i] = value;
            Log?.Invoke($"{player} のペナルティトークン: {(value ? "獲得" : "消滅")}");
            OnPenaltyChanged?.Invoke(player, value);
        }

        private void ClearState()
        {
            _itemIds = null;
            _firstPlayer = PlayerId.None;
            CurrentDescriber = PlayerId.None;
            TurnNumber = 0;
            _topicDeck.Clear();
            _topicCursor = 0;

            for (var i = 0; i < 2; i++)
            {
                _secretItemIds[i] = -1;
                _secretSlotIndices[i] = -1;
                _hasPenalty[i] = false;
                _pendingSlots[i] = -1;
                _finalSubmitted[i] = false;
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

        /// <summary>順次進行の区間で phase と手番が一致しているかを確認します。</summary>
        private bool Expect(Phase phase, PlayerId player)
        {
            if (!IsPlaying)
            {
                Log?.Invoke("入力を無視: 進行中ではありません。");
                return false;
            }

            if (CurrentPhase != phase)
            {
                Log?.Invoke($"入力を無視: phase={CurrentPhase} expected={phase}");
                return false;
            }

            if (player != CurrentTurn)
            {
                Log?.Invoke($"入力を無視: player={player} expected={CurrentTurn}");
                return false;
            }

            return true;
        }

        private bool ValidateSlot(int slotIndex)
        {
            if (_itemIds != null && slotIndex >= 0 && slotIndex < _itemIds.Length) return true;
            Log?.Invoke($"入力を無視: 不正なスロット {slotIndex}");
            return false;
        }

        // -------------------------------------------------------
        //  動作確認用
        // -------------------------------------------------------

        /// <summary>
        /// Unity なしで確認するための入口です。
        /// コンソールプロジェクトで Log を Console.WriteLine につないで呼べば動きます。
        /// </summary>
        public void RunSelfTest()
        {
            var board = new int[12];
            for (var i = 0; i < board.Length; i++) board[i] = i;

            // ケース1: 1 ターン目で正解 → 即座に終了
            var c1 = NewForTest();
            var c1Winner = PlayerId.None;
            c1.OnGameFinished += info => c1Winner = info.Winner;
            c1.StartGame(board, PlayerId.Player1);
            c1.SubmitSecretItem(PlayerId.Player1, 3);
            c1.SubmitSecretItem(PlayerId.Player2, 7);
            GuessOnce(c1, PlayerId.Player1, 7);   // Player2 の正解は itemId 7
            Log?.Invoke($"ケース1: winner={c1Winner}（Player1 なら正しい） / IsPlaying={c1.IsPlaying}（False なら正しい）");

            // ケース2: 不正解 → トークン獲得 → 次に説明する番でお題 2 個 → トークン消滅
            var c2 = NewForTest();
            var topicCount = 0;
            c2.StartGame(board, PlayerId.Player1);
            c2.SubmitSecretItem(PlayerId.Player1, 3);
            c2.SubmitSecretItem(PlayerId.Player2, 7);
            GuessOnce(c2, PlayerId.Player1, 0);   // 外す
            Log?.Invoke($"ケース2: Player1 のトークン={c2.HasPenalty(PlayerId.Player1)}（True なら正しい）");
            c2.OnTopicPresented += (p, t) => topicCount++;
            c2.SubmitAnswerDecision(PlayerId.Player2, false);  // 2 ターン目を見送り → 3 ターン目で Player1 が説明
            Log?.Invoke($"ケース2: 3 ターン目のお題数={topicCount}（2 なら正しい） / " +
                        $"Player1 のトークン={c2.HasPenalty(PlayerId.Player1)}（False なら正しい）");

            // ケース3: 誰も当てずに 10 ターン終了 → 追加ターン → 両者不正解 → 引き分け
            var c3 = NewForTest();
            var c3Reason = GameEndReason.Normal;
            var extraTurnFired = false;
            c3.OnTurnStarted += (turn, max, isExtra) => { if (isExtra) extraTurnFired = true; };
            c3.OnGameFinished += info => c3Reason = info.Reason;
            c3.StartGame(board, PlayerId.Player1);
            c3.SubmitSecretItem(PlayerId.Player1, 3);
            c3.SubmitSecretItem(PlayerId.Player2, 7);

            for (var turn = 1; turn <= 10; turn++)
            {
                c3.SubmitAnswerDecision(c3.CurrentTurn, false);   // 全ターン見送り
            }

            Log?.Invoke($"ケース3: 追加ターンに入った={extraTurnFired}（True なら正しい） / phase={c3.CurrentPhase}");

            c3.SubmitItemSelection(PlayerId.Player1, 0);
            c3.SubmitConfirmation(PlayerId.Player1, true);
            Log?.Invoke($"ケース3: 片方だけ確定した時点の IsPlaying={c3.IsPlaying}（True なら正しい）");
            c3.SubmitItemSelection(PlayerId.Player2, 1);
            c3.SubmitConfirmation(PlayerId.Player2, true);
            Log?.Invoke($"ケース3: reason={c3Reason}（Draw なら正しい）");

            // ケース4: 再戦を 2 回繰り返しても状態が残らないか
            var c4 = NewForTest();
            for (var i = 0; i < 2; i++)
            {
                c4.StartGame(board, PlayerId.Player1);
                c4.SubmitSecretItem(PlayerId.Player1, 3);
                c4.SubmitSecretItem(PlayerId.Player2, 7);
                GuessOnce(c4, PlayerId.Player1, 7);
                c4.ResetForNewRound();
            }
            Log?.Invoke($"ケース4: 初期化後 turn={c4.TurnNumber} phase={c4.CurrentPhase} " +
                        $"Player1 のトークン={c4.HasPenalty(PlayerId.Player1)}（0 / Idle / False なら正しい）");
        }

        private GameController NewForTest()
        {
            return new GameController
            {
                TopicCount = TopicCount,
                MaxTurnCount = MaxTurnCount,
                RandomSeed = 12345,
                Log = Log
            };
        }

        private static void GuessOnce(GameController c, PlayerId player, int slotIndex)
        {
            c.SubmitAnswerDecision(player, true);
            c.SubmitItemSelection(player, slotIndex);
            c.SubmitConfirmation(player, true);
        }
    }
}