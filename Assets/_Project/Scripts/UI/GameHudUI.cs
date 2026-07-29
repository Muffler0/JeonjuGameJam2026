using System.Collections.Generic;
using System.Text;
using Project.Core.Network;
using Project.Data;
using Project.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 화면 상단의 턴 정보, 상황 안내, 주제, 제한 시간을 담당한다.
    ///
    /// 제한 시간은 룰 계층이 아니라 GameBoardUI가 굴리므로 그쪽 이벤트를 받는다.
    /// </summary>
    public class GameHudUI : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private NetworkBridge bridge;
        [SerializeField] private GameBoardUI board;
        [SerializeField] private TopicLibrary topicLibrary;

        [Header("상단 정보")]
        [SerializeField] private TMP_Text turnText;
        [SerializeField] private TMP_Text describerText;

        [Header("주제")]
        [SerializeField] private TMP_Text topicText;

        [Tooltip("내 차례가 아닐 때 주제를 흐리게 하는 정도")]
        [Range(0.2f, 1f)]
        [SerializeField] private float inactiveTopicAlpha = 0.45f;

        [Header("제한 시간")]
        [SerializeField] private GameObject timerGroup;
        [SerializeField] private Image timerFill;
        [SerializeField] private TMP_Text timerText;

        [SerializeField] private Color timerNormalColor = Color.white;
        [SerializeField] private Color timerWarningColor = Color.red;

        [Tooltip("남은 시간이 이 값 이하면 경고색으로 바뀐다")]
        [SerializeField] private float timerWarningThreshold = 5f;

        // -------------------------------------------------------
        //  상태
        // -------------------------------------------------------

        private readonly List<int> _currentTopics = new List<int>();
        private readonly StringBuilder _builder = new StringBuilder();

        private PlayerId _describer = PlayerId.None;
        private GameController.Phase _phase = GameController.Phase.Idle;

        private PlayerId Local { get { return bridge != null ? bridge.LocalPlayerId : PlayerId.None; } }

        // -------------------------------------------------------
        //  초기화
        // -------------------------------------------------------

        private void Awake()
        {
            HideTimer();
            SetTopics();
        }

        private void OnEnable()
        {
            if (bridge != null)
            {
                bridge.OnBoardReady += HandleBoardReady;
                bridge.OnTurnStarted += HandleTurnStarted;
                bridge.OnTopicPresented += HandleTopicPresented;
                bridge.OnTurnChanged += HandleTurnChanged;
                bridge.OnGameFinished += HandleGameFinished;
            }

            if (board != null)
            {
                board.OnTimerUpdated += HandleTimerUpdated;
                board.OnTimerHidden += HideTimer;
            }
        }

        private void OnDisable()
        {
            if (bridge != null)
            {
                bridge.OnBoardReady -= HandleBoardReady;
                bridge.OnTurnStarted -= HandleTurnStarted;
                bridge.OnTopicPresented -= HandleTopicPresented;
                bridge.OnTurnChanged -= HandleTurnChanged;
                bridge.OnGameFinished -= HandleGameFinished;
            }

            if (board != null)
            {
                board.OnTimerUpdated -= HandleTimerUpdated;
                board.OnTimerHidden -= HideTimer;
            }
        }

        // -------------------------------------------------------
        //  브리지 이벤트
        // -------------------------------------------------------

        private void HandleBoardReady(int[] itemIds, PlayerId firstPlayer)
        {
            _describer = PlayerId.None;
            _phase = GameController.Phase.SecretSelection;

            _currentTopics.Clear();
            SetTopics();

            if (turnText != null) turnText.text = "Get Ready";
            RefreshDescriberText();
        }

        private void HandleTurnStarted(TurnInfo info)
        {
            _describer = info.Describer;

            // 새 턴이 시작되면 이전 주제를 지운다.
            _currentTopics.Clear();
            SetTopics();

            if (turnText == null) return;

            turnText.text = info.IsExtraTurn
                ? "Final Guess"
                : $"Turn {info.TurnNumber} / {info.MaxTurnCount}";
        }

        private void HandleTopicPresented(PlayerId presenter, int topicId)
        {
            // 페널티가 있으면 한 턴에 두 번 호출된다.
            _currentTopics.Add(topicId);
            SetTopics();
        }

        private void HandleTurnChanged(PlayerId player, GameController.Phase phase)
        {
            _phase = phase;
            RefreshDescriberText();
            RefreshTopicEmphasis();
        }

        private void HandleGameFinished(GameResult result)
        {
            _phase = GameController.Phase.Finished;

            HideTimer();
            _currentTopics.Clear();
            SetTopics();

            if (describerText != null) describerText.text = string.Empty;
        }

        // -------------------------------------------------------
        //  주제 표시
        // -------------------------------------------------------

        private void SetTopics()
        {
            if (topicText == null) return;

            _builder.Clear();

            for (var i = 0; i < _currentTopics.Count; i++)
            {
                if (i > 0) _builder.AppendLine();
                _builder.Append(topicLibrary != null
                    ? topicLibrary.GetTopic(_currentTopics[i])
                    : $"Topic {_currentTopics[i]}");
            }

            topicText.text = _builder.ToString();
            RefreshTopicEmphasis();
        }

        /// <summary>
        /// 실제로 답해야 하는 건 설명하는 쪽이므로, 그쪽에게만 진하게 보여준다.
        /// 상단 안내와 중복이지만 시선이 주제에 가 있을 때 헷갈리지 않게 하는 장치다.
        /// </summary>
        private void RefreshTopicEmphasis()
        {
            if (topicText == null) return;

            var isMine = _describer != PlayerId.None && _describer == Local;

            var color = topicText.color;
            color.a = isMine ? 1f : inactiveTopicAlpha;
            topicText.color = color;

            topicText.fontStyle = isMine ? FontStyles.Bold : FontStyles.Normal;
        }

        // -------------------------------------------------------
        //  상황 안내
        // -------------------------------------------------------

        private void RefreshDescriberText()
        {
            if (describerText == null) return;

            switch (_phase)
            {
                case GameController.Phase.SecretSelection:
                    describerText.text = "Pick your toy";
                    break;

                case GameController.Phase.FinalGuess:
                    describerText.text = "Make your final guess";
                    break;

                case GameController.Phase.AnswerDecision:
                case GameController.Phase.ItemSelection:
                case GameController.Phase.Confirmation:
                    // 2인 게임이라 설명하는 쪽이 아니면 추측하는 쪽이다.
                    describerText.text = _describer == Local
                        ? "Describe your toy"
                        : "Guess or pass";
                    break;

                default:
                    describerText.text = string.Empty;
                    break;
            }
        }

        // -------------------------------------------------------
        //  제한 시간
        // -------------------------------------------------------

        private void HandleTimerUpdated(float remaining, float total)
        {
            if (timerGroup != null && !timerGroup.activeSelf) timerGroup.SetActive(true);

            var isWarning = remaining <= timerWarningThreshold;

            if (timerFill != null)
            {
                timerFill.fillAmount = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;
                timerFill.color = isWarning ? timerWarningColor : timerNormalColor;
            }

            if (timerText != null)
            {
                // 9.4초 남았으면 "9"보다 "10"이 자연스럽다.
                timerText.text = Mathf.Max(0, Mathf.CeilToInt(remaining)).ToString();
                // timerText.color = isWarning ? timerWarningColor : timerNormalColor;
            }
        }

        private void HideTimer()
        {
            if (timerGroup != null) timerGroup.SetActive(false);
        }
    }
}
