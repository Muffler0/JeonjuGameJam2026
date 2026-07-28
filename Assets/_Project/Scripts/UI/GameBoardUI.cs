using System;
using System.Collections.Generic;
using Project.Core.Network;
using Project.Data;
using Project.Gameplay;
using UnityEngine;

namespace Project.UI
{
    /// <summary>
    /// 보드 타일들을 관리하고, 브리지 이벤트에 맞춰 표시와 클릭을 제어한다.
    ///
    /// 타일은 phase를 모르고, 이 클래스가 지금 클릭이 유효한지 판단한다.
    /// 룰 계층에 시간 개념이 없으므로 제한 시간도 여기서 책임진다.
    /// </summary>
    public class GameBoardUI : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private NetworkBridge bridge;
        [SerializeField] private ToyLibrary toyLibrary;

        [Tooltip("보드에 배치한 타일 12개. 순서는 상관없다(각자 슬롯 번호를 갖는다).")]
        [SerializeField] private List<BoardTile> tiles = new List<BoardTile>();

        [Header("제한 시간")]
        [Tooltip("장난감 선택 시간(초). 지나면 임의 슬롯으로 자동 제출된다.")]
        [SerializeField] private float secretSelectSeconds = 10f;

        [Tooltip("추가 턴의 최종 추측 시간(초).")]
        [SerializeField] private float finalGuessSeconds = 20f;

        // -------------------------------------------------------
        //  다른 UI가 구독할 이벤트
        // -------------------------------------------------------

        /// <summary>후보를 골랐다. 확인 다이얼로그를 띄울 것. (슬롯, 아이템 ID)</summary>
        public event Action<int, int> OnCandidateSelected;

        /// <summary>제한 시간이 갱신됐다. (남은 시간, 전체 시간)</summary>
        public event Action<float, float> OnTimerUpdated;

        /// <summary>제한 시간 표시를 감춰야 한다.</summary>
        public event Action OnTimerHidden;

        // -------------------------------------------------------
        //  상태
        // -------------------------------------------------------

        private readonly Dictionary<int, BoardTile> _tileBySlot = new();
        private int[] _itemIds;

        private GameController.Phase _phase = GameController.Phase.Idle;
        private PlayerId _turnPlayer = PlayerId.None;

        // 룰 계층은 "네 선택을 받았다"는 통지를 하지 않으므로 로컬에서 기억한다.
        private int _mySecretSlot = -1;
        private int _candidateSlot = -1;
        private bool _finalSubmitted;
        private bool _myPenalty;

        private float _timerRemaining;
        private float _timerTotal;
        private bool _timerRunning;

        private PlayerId Local { get { return bridge != null ? bridge.LocalPlayerId : PlayerId.None; } }

        // -------------------------------------------------------
        //  초기화
        // -------------------------------------------------------

        private void Awake()
        {
            foreach (var tile in tiles)
            {
                if (tile == null) continue;

                if (_tileBySlot.ContainsKey(tile.SlotIndex))
                {
                    Debug.LogError($"[GameBoardUI] 슬롯 번호가 중복됩니다: {tile.SlotIndex} ({tile.name})");
                    continue;
                }

                _tileBySlot.Add(tile.SlotIndex, tile);
                tile.OnClicked += HandleTileClicked;
            }
        }

        private void OnEnable()
        {
            if (bridge == null) return;

            bridge.OnBoardReady += HandleBoardReady;
            bridge.OnTurnStarted += HandleTurnStarted;
            bridge.OnTurnChanged += HandleTurnChanged;
            bridge.OnJudged += HandleJudged;
            bridge.OnPenaltyChanged += HandlePenaltyChanged;
            bridge.OnGameFinished += HandleGameFinished;
        }

        private void OnDisable()
        {
            if (bridge == null) return;

            bridge.OnBoardReady -= HandleBoardReady;
            bridge.OnTurnStarted -= HandleTurnStarted;
            bridge.OnTurnChanged -= HandleTurnChanged;
            bridge.OnJudged -= HandleJudged;
            bridge.OnPenaltyChanged -= HandlePenaltyChanged;
            bridge.OnGameFinished -= HandleGameFinished;
        }

        private void OnDestroy()
        {
            foreach (var tile in tiles)
            {
                if (tile != null) tile.OnClicked -= HandleTileClicked;
            }
        }

        // -------------------------------------------------------
        //  제한 시간
        // -------------------------------------------------------

        private void Update()
        {
            if (!_timerRunning) return;

            _timerRemaining -= Time.deltaTime;
            OnTimerUpdated?.Invoke(Mathf.Max(0f, _timerRemaining), _timerTotal);

            if (_timerRemaining > 0f) return;

            _timerRunning = false;
            OnTimerHidden?.Invoke();
            HandleTimeout();
        }

        private void StartTimer(float seconds)
        {
            _timerTotal = seconds;
            _timerRemaining = seconds;
            _timerRunning = true;
            OnTimerUpdated?.Invoke(seconds, seconds);
        }

        private void StopTimer()
        {
            if (!_timerRunning) return;

            _timerRunning = false;
            OnTimerHidden?.Invoke();
        }

        /// <summary>
        /// 시간이 다 됐을 때의 자동 제출.
        /// 각자 자기 것만 처리하므로 클라이언트 간 시간 오차는 문제되지 않는다.
        /// </summary>
        private void HandleTimeout()
        {
            if (_phase == GameController.Phase.SecretSelection && _mySecretSlot < 0)
            {
                int slot = PickRandomSlot();
                _mySecretSlot = slot;
                bridge.RequestSecretItem(slot);
                RefreshTiles();
                return;
            }

            if (_phase == GameController.Phase.FinalGuess && !_finalSubmitted)
            {
                int slot = _candidateSlot >= 0 ? _candidateSlot : PickRandomSlot();

                _finalSubmitted = true;
                bridge.RequestItemSelection(slot);
                bridge.RequestConfirmation(true);
                RefreshTiles();
            }
        }

        private int PickRandomSlot()
        {
            if (_itemIds != null && _itemIds.Length > 0)
            {
                return UnityEngine.Random.Range(0, _itemIds.Length);
            }

            return 0;
        }

        // -------------------------------------------------------
        //  브리지 이벤트
        // -------------------------------------------------------

        private void HandleBoardReady(int[] itemIds, PlayerId firstPlayer)
        {
            _itemIds = itemIds;
            _mySecretSlot = -1;
            _candidateSlot = -1;
            _finalSubmitted = false;
            _myPenalty = false;
            _phase = GameController.Phase.SecretSelection;

            for (int slot = 0; slot < itemIds.Length; slot++)
            {
                if (!_tileBySlot.TryGetValue(slot, out var tile)) continue;

                int itemId = itemIds[slot];
                tile.SetToy(toyLibrary != null ? toyLibrary.GetSprite(itemId) : null,
                            toyLibrary != null ? toyLibrary.GetName(itemId) : string.Empty);
            }

            if (itemIds.Length != _tileBySlot.Count)
            {
                Debug.LogWarning($"[GameBoardUI] 타일 수({_tileBySlot.Count})와 보드 크기({itemIds.Length})가 다릅니다.");
            }

            RefreshTiles();
            StartTimer(secretSelectSeconds);
        }

        private void HandleTurnStarted(TurnInfo info)
        {
            if (info.IsExtraTurn)
            {
                _candidateSlot = -1;
                _finalSubmitted = false;
                StartTimer(finalGuessSeconds);
                return;
            }

            // 일반 턴에는 제한 시간을 두지 않는다. 설명에 걸리는 시간이 사람마다 다르기 때문.
            StopTimer();
        }

        private void HandleTurnChanged(PlayerId player, GameController.Phase phase)
        {
            _turnPlayer = player;
            _phase = phase;

            // 장난감 선택이 끝나면 선택 타이머도 끝난다.
            if (phase != GameController.Phase.SecretSelection) StopTimer();

            // 확인 단계를 벗어나면 후보 표시를 지운다.
            if (phase != GameController.Phase.Confirmation && phase != GameController.Phase.FinalGuess)
            {
                _candidateSlot = -1;
            }

            RefreshTiles();
        }

        private void HandleJudged(JudgeReport report)
        {
            if (report.Answerer == Local && !report.Correct)
            {
                _candidateSlot = -1;
            }

            RefreshTiles();
        }

        private void HandlePenaltyChanged(PlayerId player, bool has)
        {
            if (player != Local) return;

            _myPenalty = has;
            RefreshTiles();
        }

        private void HandleGameFinished(GameResult result)
        {
            _phase = GameController.Phase.Finished;
            StopTimer();
            RefreshTiles();
        }

        // -------------------------------------------------------
        //  클릭 처리
        // -------------------------------------------------------

        private void HandleTileClicked(int slotIndex)
        {
            if (bridge == null) return;

            switch (_phase)
            {
                case GameController.Phase.SecretSelection:
                    if (_mySecretSlot >= 0) return;

                    // 룰 계층이 수락 통지를 주지 않으므로 로컬에서 먼저 반영한다.
                    _mySecretSlot = slotIndex;
                    bridge.RequestSecretItem(slotIndex);
                    StopTimer();
                    break;

                case GameController.Phase.ItemSelection:
                    if (_turnPlayer != Local) return;

                    _candidateSlot = slotIndex;
                    bridge.RequestItemSelection(slotIndex);
                    NotifyCandidate(slotIndex);
                    break;

                case GameController.Phase.FinalGuess:
                    if (_finalSubmitted) return;

                    _candidateSlot = slotIndex;
                    bridge.RequestItemSelection(slotIndex);
                    NotifyCandidate(slotIndex);
                    break;

                default:
                    return;
            }

            RefreshTiles();
        }

        private void NotifyCandidate(int slotIndex)
        {
            int itemId = (_itemIds != null && slotIndex < _itemIds.Length) ? _itemIds[slotIndex] : -1;
            OnCandidateSelected?.Invoke(slotIndex, itemId);
        }

        /// <summary>확인 다이얼로그에서 확정했을 때 호출한다.</summary>
        public void ConfirmCandidate()
        {
            if (_phase == GameController.Phase.FinalGuess)
            {
                _finalSubmitted = true;
                StopTimer();
            }

            bridge.RequestConfirmation(true);
            RefreshTiles();
        }

        /// <summary>확인 다이얼로그에서 취소했을 때 호출한다.</summary>
        public void CancelCandidate()
        {
            _candidateSlot = -1;
            bridge.RequestConfirmation(false);
            RefreshTiles();
        }

        // -------------------------------------------------------
        //  표시 갱신
        // -------------------------------------------------------

        private void RefreshTiles()
        {
            bool clickable = IsBoardClickable();

            foreach (var pair in _tileBySlot)
            {
                int slot = pair.Key;
                var tile = pair.Value;

                if (_phase == GameController.Phase.Finished)
                {
                    tile.SetState(BoardTile.TileState.Disabled);
                    continue;
                }

                if (slot == _mySecretSlot)
                {
                    tile.SetState(BoardTile.TileState.MySecret);
                    continue;
                }

                if (slot == _candidateSlot)
                {
                    tile.SetState(BoardTile.TileState.Candidate);
                    continue;
                }

                tile.SetState(clickable ? BoardTile.TileState.Normal : BoardTile.TileState.Dimmed);
            }
        }

        /// <summary>지금 보드를 클릭할 수 있는 상황인지.</summary>
        private bool IsBoardClickable()
        {
            switch (_phase)
            {
                case GameController.Phase.SecretSelection:
                    return _mySecretSlot < 0;

                case GameController.Phase.ItemSelection:
                    // 페널티 토큰 보유 중에는 애초에 이 단계까지 오지 않지만 방어해 둔다.
                    return _turnPlayer == Local && !_myPenalty;

                case GameController.Phase.FinalGuess:
                    return !_finalSubmitted;

                default:
                    return false;
            }
        }
    }
}
