using System;
using System.Collections;
using Project.Core.Network;
using Project.Core.SceneFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// MatchPanel과 WaitingPanel을 함께 관리한다.
    /// 두 패널은 하나의 흐름이라 스크립트를 나누면 서로 참조하느라 얽힌다.
    ///
    /// NetworkManager가 발생시키는 이벤트만 구독하고,
    /// 접속 로직 자체는 전혀 갖지 않는다.
    /// </summary>
    public class MatchmakingUI : MonoBehaviour
    {
        // 복사 안내 문구가 유지되는 시간(초).
        private const float CopyFeedbackDuration = 2f;

        // 남은 시간이 이보다 적으면 취소를 막는다. 취소와 씬 전환이 겹치는 것을 방지.
        private const double CancelLockThreshold = 1.0;

        [Header("패널")]
        [SerializeField] private GameObject matchPanel;
        [SerializeField] private GameObject waitingPanel;

        [Header("MatchPanel")]
        [SerializeField] private Button randomMatchButton;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private TMP_InputField codeInput;
        [SerializeField] private Button joinButton;
        [SerializeField] private TMP_Text matchStatusText;
        [SerializeField] private Button backButton;

        [Header("WaitingPanel")]
        [SerializeField] private GameObject codeGroup;
        [SerializeField] private TMP_Text codeText;
        [SerializeField] private Button copyButton;
        [SerializeField] private TMP_Text waitingStatusText;
        [SerializeField] private Button cancelButton;

        // 랜덤 매칭으로 들어왔는지. 코드 표시 여부를 여기서 결정한다.
        private bool _isRandomMatch;

        // Back으로 닫았는지 구분한다. 닫아둔 상태에서 상태 변화가 와도 다시 열지 않기 위함.
        private bool _matchFlowActive;

        private Coroutine _copyFeedbackRoutine;

        // -----------------------------------------------
        //  초기화
        // -----------------------------------------------

        private void Awake()
        {
            matchPanel.SetActive(false);
            waitingPanel.SetActive(false);

            randomMatchButton.onClick.AddListener(OnRandomMatchClicked);
            createRoomButton.onClick.AddListener(OnCreateRoomClicked);
            joinButton.onClick.AddListener(OnJoinClicked);
            backButton.onClick.AddListener(OnBackClicked);

            copyButton.onClick.AddListener(OnCopyClicked);
            cancelButton.onClick.AddListener(OnCancelClicked);

            // 소문자로 입력해도 화면에는 대문자로 보이게 한다.
            codeInput.onValueChanged.AddListener(OnCodeInputChanged);
        }

        private NetworkManager _net;

        private void OnEnable()
        {
            _net = NetworkManager.Instance;
            _net.OnStateChanged += HandleStateChanged;

            _net.OnStateChanged += HandleStateChanged;
            _net.OnError += HandleError;
            _net.OnOpponentJoined += HandleOpponentJoined;
            _net.OnOpponentLeft += HandleOpponentLeft;
            _net.OnCountdownCancelled += HandleCountdownCancelled;
            _net.OnGameStart += HandleGameStart;
        }

        private void OnDisable()
        {
            if (_net == null) return;

            var net = NetworkManager.Instance;

            _net.OnStateChanged -= HandleStateChanged;
            _net.OnError -= HandleError;
            _net.OnOpponentJoined -= HandleOpponentJoined;
            _net.OnOpponentLeft -= HandleOpponentLeft;
            _net.OnCountdownCancelled -= HandleCountdownCancelled;
            _net.OnGameStart -= HandleGameStart;
        }

        private void Update()
        {
            if (NetworkManager.Instance.State != NetworkState.Countdown) return;

            double remaining = NetworkManager.Instance.CountdownRemaining;

            // 예시: 3.4초 남았으면 "3"이 아니라 "4"로 보이는 게 자연스럽다.
            int display = Mathf.Max(1, Mathf.CeilToInt((float)remaining));
            waitingStatusText.text = $"Starting in {display}...";

            cancelButton.interactable = remaining > CancelLockThreshold;
        }

        // -----------------------------------------------
        //  외부 진입점
        // -----------------------------------------------

        /// <summary>타이틀의 Start 버튼에서 호출한다.</summary>
        public void OpenMatchPanel()
        {
            _matchFlowActive = true;

            codeInput.text = string.Empty;
            SetMatchStatus(string.Empty);

            waitingPanel.SetActive(false);
            matchPanel.SetActive(true);

            RefreshMatchButtons();
        }

        // -----------------------------------------------
        //  버튼 처리
        // -----------------------------------------------

        private void OnRandomMatchClicked()
        {
            _isRandomMatch = true;
            SetMatchStatus("Searching...");
            NetworkManager.Instance.StartRandomMatch();
        }

        private void OnCreateRoomClicked()
        {
            _isRandomMatch = false;
            SetMatchStatus("Creating room...");
            NetworkManager.Instance.CreatePrivateRoom();
        }

        private void OnJoinClicked()
        {
            _isRandomMatch = false;
            SetMatchStatus("Joining...");
            NetworkManager.Instance.JoinRoomByCode(codeInput.text);
        }

        private void OnBackClicked()
        {
            _matchFlowActive = false;
            matchPanel.SetActive(false);
        }

        private void OnCancelClicked()
        {
            NetworkManager.Instance.LeaveRoom();
        }

        private void OnCopyClicked()
        {
            GUIUtility.systemCopyBuffer = codeText.text;

            // 카운트다운 중에는 남은 시간 표시가 더 중요하므로 덮어쓰지 않는다.
            // Code by Claude Opus
            if (NetworkManager.Instance.State == NetworkState.Countdown) return;

            if (_copyFeedbackRoutine != null) StopCoroutine(_copyFeedbackRoutine);
            _copyFeedbackRoutine = StartCoroutine(ShowCopyFeedback());
        }

        private IEnumerator ShowCopyFeedback()
        {
            waitingStatusText.text = "Copied!";
            yield return new WaitForSecondsRealtime(CopyFeedbackDuration);

            // 안내 문구가 없는 빈 화면이 남지 않도록 원래 메시지로 되돌린다.
            if (NetworkManager.Instance.State == NetworkState.InRoom)
            {
                waitingStatusText.text = "Waiting for opponent...";
            }

            _copyFeedbackRoutine = null;
        }

        private void OnCodeInputChanged(string value)
        {
            string upper = value.ToUpperInvariant();
            if (upper == value) return;

            // 커서 위치가 앞으로 튀지 않도록 SetTextWithoutNotify를 쓴다.
            codeInput.SetTextWithoutNotify(upper);
        }

        // -----------------------------------------------
        //  네트워크 이벤트 처리
        // -----------------------------------------------

        private void HandleStateChanged(NetworkState state)
        {
            if (!_matchFlowActive) return;

            switch (state)
            {
                case NetworkState.Disconnected:
                case NetworkState.InLobby:
                    waitingPanel.SetActive(false);
                    matchPanel.SetActive(true);
                    break;

                case NetworkState.Connecting:
                case NetworkState.Joining:
                    // 연타로 방이 여러 개 만들어지는 것을 막는다.
                    break;

                case NetworkState.InRoom:
                    matchPanel.SetActive(false);
                    waitingPanel.SetActive(true);

                    codeGroup.SetActive(!_isRandomMatch);
                    codeText.text = NetworkManager.Instance.RoomCode;
                    waitingStatusText.text = _isRandomMatch
                        ? "Searching for an opponent..."
                        : "Waiting for opponent...";

                    cancelButton.interactable = true;
                    break;

                case NetworkState.Countdown:
                    // 남은 시간 표시는 Update에서 매 프레임 갱신한다.
                    break;
            }

            RefreshMatchButtons();
        }

        private void HandleError(string message)
        {
            SetMatchStatus(message);
        }

        private void HandleOpponentJoined()
        {
            waitingStatusText.text = "Opponent joined!";
        }

        private void HandleOpponentLeft()
        {
            waitingStatusText.text = "Opponent left. Waiting again...";
        }

        private void HandleCountdownCancelled()
        {
            cancelButton.interactable = true;
        }

        private void HandleGameStart(int seed)
        {
            SceneLoader.Instance.Load(SceneName.Game, fadeOutBgm: true);
        }

        // -----------------------------------------------
        //  표시 갱신
        // -----------------------------------------------

        private void RefreshMatchButtons()
        {
            var state = NetworkManager.Instance.State;
            bool ready = state == NetworkState.Disconnected || state == NetworkState.InLobby;

            randomMatchButton.interactable = ready;
            createRoomButton.interactable = ready;
            joinButton.interactable = ready;
            codeInput.interactable = ready;
        }

        private void SetMatchStatus(string message)
        {
            if (matchStatusText != null) matchStatusText.text = message;
        }
    }
}
