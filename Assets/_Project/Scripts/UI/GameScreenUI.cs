using System.Collections.Generic;
using Project.Core.Network;
using Project.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// GameScene の画面全体を管理する。
    /// NetworkBridge のイベントだけを購読し、通信もルール判定も一切持たない。
    ///
    /// ★ GameController は「マスターのみ」に存在するため、UI から直接触ってはいけない。
    ///   必要な情報はすべて NetworkBridge のイベントで届く。
    /// </summary>
    public class GameScreenUI : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private NetworkBridge bridge;
        [SerializeField] private GameMasterData masterData;

        [Header("盤面")]
        [Tooltip("BoardSlotButton を付けたボタンを子として並べておく。並び順がスロット番号になる。")]
        [SerializeField] private Transform boardRoot;

        [Header("共通表示")]
        [SerializeField] private TMP_Text turnText;
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private GameObject finalPickBanner;
        [SerializeField] private GameObject waitingOverlay;
        [SerializeField] private TMP_Text waitingText;

        [Header("お題")]
        [SerializeField] private GameObject topicPanel;
        [SerializeField] private TMP_Text topicText;

        [Tooltip("お題を相手にも見せるか。オフなら出題側だけに表示する。")]
        [SerializeField] private bool showTopicToBothPlayers;

        [Header("解答するか選ぶ")]
        [SerializeField] private GameObject decisionPanel;
        [SerializeField] private Button answerButton;
        [SerializeField] private Button passButton;

        [Header("確認ダイアログ")]
        [SerializeField] private GameObject confirmPanel;
        [SerializeField] private TMP_Text confirmText;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;

        [Header("結果")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TMP_Text resultTitleText;
        [SerializeField] private TMP_Text resultDetailText;
        [SerializeField] private TMP_Text rematchStatusText;
        [SerializeField] private Button rematchButton;
        [SerializeField] private Button leaveButton;

        // -------------------------------------------------------
        //  状態
        // -------------------------------------------------------

        private readonly List<BoardSlotButton> _slots = new List<BoardSlotButton>();
        private readonly List<int> _currentTopics = new List<int>();

        private int[] _boardItemIds;
        private GameController.Phase _phase = GameController.Phase.Idle;
        private PlayerId _actor = PlayerId.None;

        // 次にお題が届いたら表示を作り直すかどうか。
        // お題は 1 ハーフターンに複数回連続で届くため、単純な上書きだと 1 個目が消える。
        private bool _clearTopicsOnNextPresent = true;

        private int _pendingSlotIndex = -1;

        private bool IsMyTurn => _actor == bridge.LocalPlayerId;

        // -------------------------------------------------------
        //  初期化
        // -------------------------------------------------------

        private void Awake()
        {
            AddListener(answerButton, () => bridge.RequestAnswerDecision(true));
            AddListener(passButton, () => bridge.RequestAnswerDecision(false));
            AddListener(confirmYesButton, () => bridge.RequestConfirmation(true));
            AddListener(confirmNoButton, () => bridge.RequestConfirmation(false));
            AddListener(rematchButton, OnRematchClicked);
            AddListener(leaveButton, () => bridge.LeaveGame());

            HideAllPanels();
        }

        private void OnEnable()
        {
            if (bridge == null)
            {
                Debug.LogError("NetworkBridge が設定されていません。");
                return;
            }

            bridge.OnBoardReady += HandleBoardReady;
            bridge.OnTopicPresented += HandleTopicPresented;
            bridge.OnTurnChanged += HandleTurnChanged;
            bridge.OnJudged += HandleJudged;
            bridge.OnGameFinished += HandleGameFinished;
            bridge.OnOpponentMissing += HandleOpponentMissing;
            bridge.OnRematchVoteChanged += HandleRematchVoteChanged;
            bridge.OnRematchDeclined += HandleRematchDeclined;

            // TODO: NetworkBridge に RpcTurnStarted の中継が入ったら有効化する。
            // bridge.OnTurnStarted += ShowTurnInfo;
        }

        private void OnDisable()
        {
            if (bridge == null) return;

            bridge.OnBoardReady -= HandleBoardReady;
            bridge.OnTopicPresented -= HandleTopicPresented;
            bridge.OnTurnChanged -= HandleTurnChanged;
            bridge.OnJudged -= HandleJudged;
            bridge.OnGameFinished -= HandleGameFinished;
            bridge.OnOpponentMissing -= HandleOpponentMissing;
            bridge.OnRematchVoteChanged -= HandleRematchVoteChanged;
            bridge.OnRematchDeclined -= HandleRematchDeclined;

            // bridge.OnTurnStarted -= ShowTurnInfo;
        }

        // -------------------------------------------------------
        //  盤面
        // -------------------------------------------------------

        private void HandleBoardReady(int[] itemIds, PlayerId firstPlayer)
        {
            _boardItemIds = itemIds;
            _currentTopics.Clear();
            _clearTopicsOnNextPresent = true;
            _pendingSlotIndex = -1;

            BuildBoard(itemIds);
            HideAllPanels();
        }

        /// <summary>
        /// boardRoot の子として並べてあるボタンを、上から順にスロット 0,1,2... として使う。
        /// 位置は Unity 上で自由に置いてよい。並び順だけが意味を持つ。
        /// </summary>
        private void BuildBoard(int[] itemIds)
        {
            _slots.Clear();

            if (boardRoot == null)
            {
                Debug.LogError("boardRoot が設定されていません。");
                return;
            }

            boardRoot.GetComponentsInChildren(true, _slots);

            if (_slots.Count < itemIds.Length)
            {
                Debug.LogError($"盤面のボタンが足りません。必要 {itemIds.Length} 個 / 実際 {_slots.Count} 個");
            }

            for (var i = 0; i < _slots.Count; i++)
            {
                if (i < itemIds.Length)
                {
                    _slots[i].gameObject.SetActive(true);
                    _slots[i].Setup(
                        i,
                        masterData.GetItemName(itemIds[i]),
                        masterData.GetItemSprite(itemIds[i]),
                        HandleSlotClicked);
                }
                else
                {
                    // 余ったボタンは使わないので隠す。
                    _slots[i].gameObject.SetActive(false);
                }
            }

            SetBoardInteractable(false);
        }

        private void SetBoardInteractable(bool value)
        {
            foreach (var slot in _slots)
            {
                if (slot != null) slot.SetInteractable(value);
            }
        }

        /// <summary>
        /// 盤面クリック。同じボタンでも、いまのフェーズによって意味が変わる。
        /// </summary>
        private void HandleSlotClicked(int slotIndex)
        {
            if (!IsMyTurn) return;

            switch (_phase)
            {
                case GameController.Phase.SecretSelection:
                    bridge.RequestSecretItem(slotIndex);
                    break;

                case GameController.Phase.ItemSelection:
                    _pendingSlotIndex = slotIndex;
                    bridge.RequestItemSelection(slotIndex);
                    break;
            }
        }

        // -------------------------------------------------------
        //  お題
        // -------------------------------------------------------

        /// <summary>
        /// ★ ペナルティがあるハーフターンでは、これが同じフレームで複数回呼ばれる。
        ///   単純な上書きにすると 1 個目のお題が消えるので、溜めて表示する。
        /// </summary>
        private void HandleTopicPresented(PlayerId presenter, int topicId)
        {
            if (_clearTopicsOnNextPresent)
            {
                _currentTopics.Clear();
                _clearTopicsOnNextPresent = false;
            }

            _currentTopics.Add(topicId);

            bool visible = showTopicToBothPlayers || presenter == bridge.LocalPlayerId;
            SetActive(topicPanel, visible);

            if (topicText == null) return;

            if (!visible)
            {
                topicText.text = string.Empty;
                return;
            }

            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < _currentTopics.Count; i++)
            {
                if (i > 0) builder.Append('\n');

                if (_currentTopics.Count > 1) builder.Append($"({i + 1}) ");
                builder.Append(masterData.GetTopicText(_currentTopics[i]));
            }

            if (_currentTopics.Count > 1)
            {
                builder.Append("\n※前回のミスによりお題が増えています");
            }

            topicText.text = builder.ToString();
        }

        // -------------------------------------------------------
        //  フェーズ切り替え
        // -------------------------------------------------------

        private void HandleTurnChanged(PlayerId player, GameController.Phase phase)
        {
            _actor = player;
            _phase = phase;

            // 解答判断まで進んだら、次に届くお題は新しいハーフターンのもの。
            if (phase == GameController.Phase.AnswerDecision) _clearTopicsOnNextPresent = true;

            HideAllPanels();

            switch (phase)
            {
                case GameController.Phase.SecretSelection:
                    ShowPhase("自分の正解アイテムを選んでください", "相手が正解アイテムを選んでいます");
                    SetBoardInteractable(IsMyTurn);
                    break;

                case GameController.Phase.AnswerDecision:
                    ShowPhase("解答しますか？", "相手が考えています");
                    SetActive(decisionPanel, IsMyTurn);
                    SetBoardInteractable(false);
                    break;

                case GameController.Phase.ItemSelection:
                    ShowPhase("相手の正解だと思うアイテムを選択", "相手が解答を選んでいます");
                    SetBoardInteractable(IsMyTurn);
                    break;

                case GameController.Phase.Confirmation:
                    ShowPhase("この解答で確定しますか？", "相手が確認中です");
                    SetActive(confirmPanel, IsMyTurn);
                    SetBoardInteractable(false);

                    if (IsMyTurn && confirmText != null)
                    {
                        var itemId = _boardItemIds != null
                                     && _pendingSlotIndex >= 0
                                     && _pendingSlotIndex < _boardItemIds.Length
                            ? _boardItemIds[_pendingSlotIndex]
                            : -1;

                        confirmText.text = $"「{masterData.GetItemName(itemId)}」で確定しますか？";
                    }

                    break;

                case GameController.Phase.Finished:
                    SetBoardInteractable(false);
                    break;
            }
        }

        private void ShowPhase(string myMessage, string opponentMessage)
        {
            if (phaseText != null) phaseText.text = IsMyTurn ? myMessage : opponentMessage;

            SetActive(waitingOverlay, !IsMyTurn);
            if (waitingText != null) waitingText.text = opponentMessage;
        }

        // -------------------------------------------------------
        //  ターン表示 / Final Pick
        // -------------------------------------------------------

        /// <summary>
        /// NetworkBridge の OnTurnStarted から呼ばれる想定。
        /// 中継が入るまでは呼ばれないので、Final Pick の見た目は
        /// Inspector で finalPickBanner を手動 ON にして確認できる。
        /// </summary>
        public void ShowTurnInfo(int turn, int maxTurn, bool isFinal)
        {
            if (turnText != null)
            {
                turnText.text = maxTurn > 0 ? $"{turn} / {maxTurn}" : $"{turn}";
            }

            SetActive(finalPickBanner, isFinal);
        }

        // -------------------------------------------------------
        //  判定
        // -------------------------------------------------------

        /// <summary>
        /// ★ Correct が true でも画面遷移してはいけない。
        ///   前半で正解しても後半の相手に解答機会が残る（両者正解なら引き分け）。
        ///   結果画面への遷移は OnGameFinished のみ。
        /// </summary>
        private void HandleJudged(JudgeReport report)
        {
            var itemName = masterData.GetItemName(report.ItemId);
            var who = report.Answerer == bridge.LocalPlayerId ? "あなた" : "相手";

            if (phaseText != null)
            {
                phaseText.text = report.Correct
                    ? $"{who}の解答「{itemName}」→ 正解！"
                    : $"{who}の解答「{itemName}」→ 不正解";
            }

            if (report.SlotIndex >= 0 && report.SlotIndex < _slots.Count)
            {
                _slots[report.SlotIndex].SetHighlight(true);
            }

            _pendingSlotIndex = -1;
        }

        // -------------------------------------------------------
        //  結果
        // -------------------------------------------------------

        private void HandleGameFinished(GameResult result)
        {
            HideAllPanels();
            SetBoardInteractable(false);
            SetActive(resultPanel, true);

            if (resultTitleText != null) resultTitleText.text = BuildResultTitle(result);
            if (resultDetailText != null) resultDetailText.text = BuildResultDetail(result);

            if (rematchStatusText != null) rematchStatusText.text = string.Empty;
            if (rematchButton != null) rematchButton.interactable = true;
        }

        private string BuildResultTitle(GameResult result)
        {
            if (result.Info.Reason == GameEndReason.OpponentLeft) return "相手が退出しました";
            if (result.Info.Reason == GameEndReason.Draw) return "引き分け";

            return result.Info.Winner == bridge.LocalPlayerId ? "あなたの勝ち" : "あなたの負け";
        }

        private string BuildResultDetail(GameResult result)
        {
            // 中断時は正解が -1 で届くため、表示しない。
            if (result.Info.Reason == GameEndReason.OpponentLeft) return string.Empty;

            var mine = bridge.LocalPlayerId == PlayerId.Player1
                ? result.Player1SecretItemId
                : result.Player2SecretItemId;

            var theirs = bridge.LocalPlayerId == PlayerId.Player1
                ? result.Player2SecretItemId
                : result.Player1SecretItemId;

            return $"あなたの正解: {masterData.GetItemName(mine)}\n"
                   + $"相手の正解: {masterData.GetItemName(theirs)}";
        }

        private void HandleOpponentMissing()
        {
            HideAllPanels();
            SetActive(resultPanel, true);

            if (resultTitleText != null) resultTitleText.text = "相手が退出しました";
            if (resultDetailText != null) resultDetailText.text = string.Empty;
            if (rematchButton != null) rematchButton.interactable = false;
        }

        // -------------------------------------------------------
        //  再戦
        // -------------------------------------------------------

        private void OnRematchClicked()
        {
            if (rematchButton != null) rematchButton.interactable = false;
            if (rematchStatusText != null) rematchStatusText.text = "相手の応答を待っています...";

            bridge.RequestRematch(true);
        }

        private void HandleRematchVoteChanged(int voted, int total)
        {
            if (rematchStatusText != null) rematchStatusText.text = $"再戦 {voted} / {total}";
        }

        private void HandleRematchDeclined()
        {
            if (rematchStatusText != null) rematchStatusText.text = "再戦は成立しませんでした";
            if (rematchButton != null) rematchButton.interactable = false;
        }

        // -------------------------------------------------------
        //  補助
        // -------------------------------------------------------

        private void HideAllPanels()
        {
            SetActive(decisionPanel, false);
            SetActive(confirmPanel, false);
            SetActive(resultPanel, false);
            SetActive(waitingOverlay, false);
            SetActive(finalPickBanner, false);
        }

        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value) target.SetActive(value);
        }

        private void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.AddListener(action);
        }
    }
}
