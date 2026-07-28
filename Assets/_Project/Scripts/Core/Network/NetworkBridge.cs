using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using Project.Gameplay;
using UnityEngine;
using Random = System.Random;

namespace Project.Core.Network
{
    /// <summary>
    /// 게임 결과. GameFinishedInfo에 더해 양쪽의 정답 아이템을 함께 담는다.
    ///
    /// 룰 계층(GameController)은 마스터에만 존재하므로,
    /// 클라이언트는 GetSecretItemId()를 호출할 수 없다.
    /// 결과 화면에서 정답을 보여주려면 이렇게 실어서 보내야 한다.
    /// </summary>
    public struct GameResult
    {
        public GameFinishedInfo Info;

        /// <summary>Player1의 정답 아이템 ID. 알 수 없으면 -1.</summary>
        public int Player1SecretItemId;

        /// <summary>Player2의 정답 아이템 ID. 알 수 없으면 -1.</summary>
        public int Player2SecretItemId;
    }

    /// <summary>
    /// 턴 시작 정보. 클라이언트는 룰 계층이 없으므로 필요한 값을 모두 실어 보낸다.
    /// </summary>
    public struct TurnInfo
    {
        public int TurnNumber;
        public int MaxTurnCount;

        /// <summary>추가 턴(동시 추측)인지.</summary>
        public bool IsExtraTurn;

        /// <summary>이번 턴에 설명하는 쪽. 추가 턴에서는 None.</summary>
        public PlayerId Describer;
    }

    /// <summary>
    /// GameController(룰 계층)와 Photon 사이를 중계한다.
    ///
    /// 룰 계층은 Photon을 전혀 모르고, 이 클래스만 양쪽을 안다.
    /// GameScene에 배치하고 PhotonView를 함께 붙일 것.
    ///
    /// 권한 구조:
    ///   - GameController는 마스터에서만 동작한다.
    ///   - 클라이언트의 입력은 RPC로 마스터에게 전달된다.
    ///   - 결과는 마스터가 전원에게 브로드캐스트한다.
    ///     (마스터 자신도 브로드캐스트로 받으므로 양쪽 처리 경로가 같다)
    ///
    /// 커뮤니케이션 기능(채팅 등)은 룰 계층에 입력 창구가 없으므로
    /// 이 클래스에 별도 RPC로 붙이면 된다.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class NetworkBridge : MonoBehaviourPunCallbacks
    {
        // 재대국 응답을 기다리는 시간(초). 상대가 자리를 뜨면 무한 대기가 되므로 필요하다.
        private const float RematchTimeout = 30f;

        [Header("보드 설정")]
        [Tooltip("보드의 타일 개수")]
        [SerializeField] private int boardSlotCount = 12;

        [Tooltip("장난감 종류 수. 타일 수보다 많으면 그중 일부만 사용된다.")]
        [SerializeField] private int itemTypeCount = 12;

        [Header("룰 설정 (StartGame 전에 GameController로 전달된다)")]
        [Tooltip("주제 풀의 크기. 10턴 + 페널티분을 감당하려면 15 이상 권장")]
        [SerializeField] private int topicCount = 15;

        [Tooltip("총 턴 수. 기획 기준 10")]
        [SerializeField] private int maxTurnCount = 10;

        // -------------------------------------------------------
        //  UI가 구독할 이벤트
        // -------------------------------------------------------

        /// <summary>보드가 준비됐다. (아이템 배열, 선공)</summary>
        public event Action<int[], PlayerId> OnBoardReady;

        /// <summary>턴이 시작됐다.</summary>
        public event Action<TurnInfo> OnTurnStarted;

        /// <summary>주제가 제시됐다. (설명하는 쪽, topicId)</summary>
        public event Action<PlayerId, int> OnTopicPresented;

        /// <summary>판정 결과가 도착했다.</summary>
        public event Action<JudgeReport> OnJudged;

        /// <summary>페널티 토큰 보유 상태가 바뀌었다. (대상, 보유 여부)</summary>
        public event Action<PlayerId, bool> OnPenaltyChanged;

        /// <summary>
        /// 입력 대기 상태가 바뀌었다. (입력할 플레이어, 요구되는 조작)
        /// 동시 진행 구간에서는 플레이어가 None으로 온다.
        /// </summary>
        public event Action<PlayerId, GameController.Phase> OnTurnChanged;

        /// <summary>게임이 끝났다. 화면 전환은 이 이벤트에서만 한다.</summary>
        public event Action<GameResult> OnGameFinished;

        /// <summary>씬에 들어온 시점에 이미 상대가 없었다.</summary>
        public event Action OnOpponentMissing;

        /// <summary>재대국 응답 현황이 바뀌었다. (응답한 사람 수, 전체)</summary>
        public event Action<int, int> OnRematchVoteChanged;

        /// <summary>재대국이 무산됐다. (거절 또는 시간 초과)</summary>
        public event Action OnRematchDeclined;

        // -------------------------------------------------------
        //  상태
        // -------------------------------------------------------

        /// <summary>이 클라이언트가 어느 플레이어인지.</summary>
        public PlayerId LocalPlayerId { get; private set; } = PlayerId.None;

        /// <summary>상대 플레이어.</summary>
        public PlayerId OpponentPlayerId
        {
            get
            {
                if (LocalPlayerId == PlayerId.Player1) return PlayerId.Player2;
                if (LocalPlayerId == PlayerId.Player2) return PlayerId.Player1;
                return PlayerId.None;
            }
        }

        /// <summary>보드 타일 수. UI가 임의 슬롯을 고를 때 쓴다.</summary>
        public int SlotCount { get { return boardSlotCount; } }

        /// <summary>마스터에서만 존재한다. 클라이언트에서는 null.</summary>
        private GameController _game;

        // ActorNumber -> PlayerId. 게임 시작 시 한 번 정하고 이후 바뀌지 않는다.
        private readonly Dictionary<int, PlayerId> _playerIdMap = new();

        private readonly Dictionary<PlayerId, bool> _rematchVotes = new();
        private Coroutine _rematchTimeoutRoutine;

        private bool _gameEnded;

        // -------------------------------------------------------
        //  시작
        // -------------------------------------------------------

        private void Start()
        {
            // 씬을 불러오는 동안 상대가 나간 경우. 게임을 시작하지 않고 바로 종료 처리한다.
            if (NetworkManager.Instance != null && NetworkManager.Instance.ConsumeOpponentLeftFlag())
            {
                _gameEnded = true;
                OnOpponentMissing?.Invoke();
                return;
            }

            AssignPlayerIds();

            if (!PhotonNetwork.IsMasterClient) return;

            CreateGameController();
            StartNewRound();
        }

        /// <summary>
        /// ActorNumber가 작은 쪽이 Player1이 된다.
        /// 먼저 들어온 사람이 낮은 번호를 받으므로 양쪽에서 같은 결과가 나온다.
        /// </summary>
        private void AssignPlayerIds()
        {
            _playerIdMap.Clear();

            int smallest = int.MaxValue;
            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (player.ActorNumber < smallest) smallest = player.ActorNumber;
            }

            foreach (var player in PhotonNetwork.PlayerList)
            {
                _playerIdMap[player.ActorNumber] =
                    player.ActorNumber == smallest ? PlayerId.Player1 : PlayerId.Player2;
            }

            LocalPlayerId = ToPlayerId(PhotonNetwork.LocalPlayer.ActorNumber);
        }

        private PlayerId ToPlayerId(int actorNumber)
        {
            return _playerIdMap.TryGetValue(actorNumber, out var id) ? id : PlayerId.None;
        }

        private void CreateGameController()
        {
            _game = new GameController();

            // 룰 계층은 Debug.Log를 쓸 수 없으므로 여기서 콘솔에 연결해 준다.
            _game.Log = message => Debug.Log($"[GameRules] {message}");

            // StartGame보다 먼저 설정해야 하는 값들.
            _game.TopicCount = topicCount;
            _game.MaxTurnCount = maxTurnCount;

            // CurrentDescriber는 OnTurnStarted 직전에 갱신되므로 여기서 읽어도 안전하다.
            _game.OnTurnStarted += (turn, max, isExtra) =>
                photonView.RPC(nameof(RpcTurnStarted), RpcTarget.All,
                    turn, max, isExtra, (int)_game.CurrentDescriber);

            _game.OnTopicPresented += (presenter, topicId) =>
                photonView.RPC(nameof(RpcTopicPresented), RpcTarget.All, (int)presenter, topicId);

            _game.OnJudged += report =>
                photonView.RPC(nameof(RpcJudged), RpcTarget.All,
                    (int)report.Answerer, report.SlotIndex, report.ItemId, report.Correct);

            _game.OnPenaltyChanged += (player, has) =>
                photonView.RPC(nameof(RpcPenaltyChanged), RpcTarget.All, (int)player, has);

            // phase는 클라이언트가 직접 읽을 수 없으므로 여기서 함께 실어 보낸다.
            _game.OnTurnChanged += next =>
                photonView.RPC(nameof(RpcTurnChanged), RpcTarget.All,
                    (int)next, (int)_game.CurrentPhase);

            _game.OnGameFinished += info =>
                photonView.RPC(nameof(RpcGameFinished), RpcTarget.All,
                    (int)info.Winner, (int)info.Reason,
                    _game.GetSecretItemId(PlayerId.Player1),
                    _game.GetSecretItemId(PlayerId.Player2));
        }

        /// <summary>마스터가 보드와 선공을 정해 전원에게 배포하고 게임을 시작한다.</summary>
        private void StartNewRound()
        {
            _gameEnded = false;
            _rematchVotes.Clear();

            int[] board = GenerateBoard();

            // 후공만 페널티 토큰 불이익을 받는 구조라, 선공을 매 판 무작위로 정한다.
            var first = UnityEngine.Random.value < 0.5f ? PlayerId.Player1 : PlayerId.Player2;

            photonView.RPC(nameof(RpcStartRound), RpcTarget.All, board, (int)first);
        }

        /// <summary>
        /// 시드를 공유해 각자 생성하지 않고, 마스터가 만든 배열을 그대로 배포한다.
        /// 환경 차이로 보드가 어긋날 여지를 없애기 위함이다.
        /// </summary>
        private int[] GenerateBoard()
        {
            // 장난감 종류에서 타일 수만큼 중복 없이 뽑는다.
            var pool = new List<int>(itemTypeCount);
            for (int i = 0; i < itemTypeCount; i++) pool.Add(i);

            var random = new Random(Guid.NewGuid().GetHashCode());
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            var board = new int[boardSlotCount];
            for (int i = 0; i < board.Length; i++)
            {
                // 종류가 타일 수보다 적으면 어쩔 수 없이 반복해서 채운다.
                board[i] = pool[i % pool.Count];
            }

            return board;
        }

        // -------------------------------------------------------
        //  UI가 호출하는 입력 (클라이언트 -> 마스터)
        // -------------------------------------------------------

        public void RequestSecretItem(int slotIndex)
        {
            photonView.RPC(nameof(RpcSubmitSecretItem), RpcTarget.MasterClient,
                (int)LocalPlayerId, slotIndex);
        }

        public void RequestAnswerDecision(bool willAnswer)
        {
            photonView.RPC(nameof(RpcSubmitAnswerDecision), RpcTarget.MasterClient,
                (int)LocalPlayerId, willAnswer);
        }

        public void RequestItemSelection(int slotIndex)
        {
            photonView.RPC(nameof(RpcSubmitItemSelection), RpcTarget.MasterClient,
                (int)LocalPlayerId, slotIndex);
        }

        public void RequestConfirmation(bool confirmed)
        {
            photonView.RPC(nameof(RpcSubmitConfirmation), RpcTarget.MasterClient,
                (int)LocalPlayerId, confirmed);
        }

        /// <summary>재대국 의사 표시. 양쪽 모두 동의해야 다시 시작한다.</summary>
        public void RequestRematch(bool wants)
        {
            photonView.RPC(nameof(RpcRematchVote), RpcTarget.MasterClient,
                (int)LocalPlayerId, wants);
        }

        /// <summary>게임을 떠난다. 상대에게는 이탈로 처리된다.</summary>
        public void LeaveGame()
        {
            NetworkManager.Instance.LeaveRoom();
        }

        // -------------------------------------------------------
        //  입력 RPC (마스터에서만 실행)
        // -------------------------------------------------------

        [PunRPC]
        private void RpcSubmitSecretItem(int playerId, int slotIndex)
        {
            _game?.SubmitSecretItem((PlayerId)playerId, slotIndex);
        }

        [PunRPC]
        private void RpcSubmitAnswerDecision(int playerId, bool willAnswer)
        {
            _game?.SubmitAnswerDecision((PlayerId)playerId, willAnswer);
        }

        [PunRPC]
        private void RpcSubmitItemSelection(int playerId, int slotIndex)
        {
            _game?.SubmitItemSelection((PlayerId)playerId, slotIndex);
        }

        [PunRPC]
        private void RpcSubmitConfirmation(int playerId, bool confirmed)
        {
            _game?.SubmitConfirmation((PlayerId)playerId, confirmed);
        }

        // -------------------------------------------------------
        //  결과 RPC (전원에서 실행)
        // -------------------------------------------------------

        [PunRPC]
        private void RpcStartRound(int[] itemIds, int firstPlayer)
        {
            _gameEnded = false;

            // StartGame 안에서 OnTurnChanged가 즉시 발화하므로,
            // 보드 준비를 먼저 알려야 UI가 순서대로 받는다.
            OnBoardReady?.Invoke(itemIds, (PlayerId)firstPlayer);

            // 룰 계층은 마스터에만 있으므로, 실제로 호출되는 것도 마스터뿐이다.
            _game?.StartGame(itemIds, (PlayerId)firstPlayer);
        }

        [PunRPC]
        private void RpcTurnStarted(int turn, int max, bool isExtra, int describer)
        {
            OnTurnStarted?.Invoke(new TurnInfo
            {
                TurnNumber = turn,
                MaxTurnCount = max,
                IsExtraTurn = isExtra,
                Describer = (PlayerId)describer
            });
        }

        [PunRPC]
        private void RpcTopicPresented(int presenter, int topicId)
        {
            OnTopicPresented?.Invoke((PlayerId)presenter, topicId);
        }

        [PunRPC]
        private void RpcJudged(int answerer, int slotIndex, int itemId, bool correct)
        {
            OnJudged?.Invoke(new JudgeReport
            {
                Answerer = (PlayerId)answerer,
                SlotIndex = slotIndex,
                ItemId = itemId,
                Correct = correct
            });
        }

        [PunRPC]
        private void RpcPenaltyChanged(int player, bool has)
        {
            OnPenaltyChanged?.Invoke((PlayerId)player, has);
        }

        [PunRPC]
        private void RpcTurnChanged(int player, int phase)
        {
            OnTurnChanged?.Invoke((PlayerId)player, (GameController.Phase)phase);
        }

        [PunRPC]
        private void RpcGameFinished(int winner, int reason, int player1Secret, int player2Secret)
        {
            if (_gameEnded) return;
            _gameEnded = true;

            OnGameFinished?.Invoke(new GameResult
            {
                Info = new GameFinishedInfo
                {
                    Winner = (PlayerId)winner,
                    Reason = (GameEndReason)reason
                },
                Player1SecretItemId = player1Secret,
                Player2SecretItemId = player2Secret
            });

            if (PhotonNetwork.IsMasterClient) BeginRematchWait();
        }

        // -------------------------------------------------------
        //  재대국
        // -------------------------------------------------------

        private void BeginRematchWait()
        {
            _rematchVotes.Clear();

            StopRematchTimeout();
            _rematchTimeoutRoutine = StartCoroutine(RematchTimeoutRoutine());
        }

        [PunRPC]
        private void RpcRematchVote(int playerId, bool wants)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            // 거절이 하나라도 나오면 더 기다릴 이유가 없다.
            if (!wants)
            {
                StopRematchTimeout();
                photonView.RPC(nameof(RpcRematchDeclined), RpcTarget.All);
                return;
            }

            _rematchVotes[(PlayerId)playerId] = true;
            photonView.RPC(nameof(RpcRematchVoteChanged), RpcTarget.All,
                _rematchVotes.Count, PhotonNetwork.CurrentRoom.PlayerCount);

            if (_rematchVotes.Count < PhotonNetwork.CurrentRoom.PlayerCount) return;

            StopRematchTimeout();

            // 씬을 다시 불러오지 않고 같은 씬에서 상태만 초기화한다.
            _game?.ResetForNewRound();
            StartNewRound();
        }

        [PunRPC]
        private void RpcRematchVoteChanged(int voted, int total)
        {
            OnRematchVoteChanged?.Invoke(voted, total);
        }

        [PunRPC]
        private void RpcRematchDeclined()
        {
            OnRematchDeclined?.Invoke();
        }

        private void StopRematchTimeout()
        {
            if (_rematchTimeoutRoutine == null) return;

            StopCoroutine(_rematchTimeoutRoutine);
            _rematchTimeoutRoutine = null;
        }

        private IEnumerator RematchTimeoutRoutine()
        {
            yield return new WaitForSecondsRealtime(RematchTimeout);

            _rematchTimeoutRoutine = null;
            photonView.RPC(nameof(RpcRematchDeclined), RpcTarget.All);
        }

        // -------------------------------------------------------
        //  이탈 처리
        // -------------------------------------------------------
        // Code by Claude Opus
        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            StopRematchTimeout();

            if (_gameEnded)
            {
                // 결과 화면에서 상대가 나간 경우. 재대국이 성립할 수 없다.
                OnRematchDeclined?.Invoke();
                return;
            }

            if (PhotonNetwork.IsMasterClient && _game != null)
            {
                // 룰 계층에도 알려서 정상적인 종료 경로를 타게 한다.
                // 중단이므로 승자는 없다(GameController도 None으로 처리한다).
                _game.AbortGame(ToPlayerId(otherPlayer.ActorNumber));
                return;
            }

            // 마스터가 나간 경우. 룰 계층이 없으므로 로컬에서 바로 종료 처리한다.
            _gameEnded = true;
            OnGameFinished?.Invoke(new GameResult
            {
                Info = new GameFinishedInfo
                {
                    Winner = PlayerId.None,
                    Reason = GameEndReason.OpponentLeft
                },
                Player1SecretItemId = -1,
                Player2SecretItemId = -1
            });
        }
    }
}