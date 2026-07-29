using Project.Core.Network;
using Project.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 내 추측 차례에 뜨는 다이얼로그.
    /// "추측하기 / 넘기기"를 고르고, 넘기기는 기획서의 "enough"(OK)에 해당한다.
    ///
    /// 브리지의 phase만 보고 스스로 열고 닫으므로 다른 UI가 관여하지 않는다.
    /// </summary>
    public class AnswerDecisionDialog : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private NetworkBridge bridge;

        [Tooltip("열고 닫을 대상. 비워두면 이 오브젝트를 사용한다.")]
        [SerializeField] private GameObject panel;

        [Header("표시 요소")]
        [SerializeField] private Button guessButton;
        [SerializeField] private Button passButton;
        [SerializeField] private TMP_Text messageText;

        [Tooltip("페널티 토큰 때문에 추측할 수 없을 때 띄울 안내")]
        [SerializeField] private TMP_Text penaltyHintText;

        private bool _hasPenalty;

        private GameObject Panel { get { return panel != null ? panel : gameObject; } }

        [SerializeField] private GameFeedbackUI feedback;

        private bool _pendingOpen;

        // -------------------------------------------------------
        //  초기화
        // -------------------------------------------------------

        private void Awake()
        {
            if (guessButton != null) guessButton.onClick.AddListener(OnGuessClicked);
            if (passButton != null) passButton.onClick.AddListener(OnPassClicked);

            Close();
        }

        private void OnEnable()
        {
            if (bridge == null) return;

            bridge.OnTurnChanged += HandleTurnChanged;
            bridge.OnPenaltyChanged += HandlePenaltyChanged;
            bridge.OnBoardReady += HandleBoardReady;
            bridge.OnGameFinished += HandleGameFinished;

            if (feedback != null) feedback.OnAllPlayed += HandleFeedbackFinished;
        }

        private void OnDisable()
        {
            if (bridge == null) return;

            bridge.OnTurnChanged -= HandleTurnChanged;
            bridge.OnPenaltyChanged -= HandlePenaltyChanged;
            bridge.OnBoardReady -= HandleBoardReady;
            bridge.OnGameFinished -= HandleGameFinished;

            if (feedback != null) feedback.OnAllPlayed -= HandleFeedbackFinished;
        }

        private void OnDestroy()
        {
            if (guessButton != null) guessButton.onClick.RemoveListener(OnGuessClicked);
            if (passButton != null) passButton.onClick.RemoveListener(OnPassClicked);
        }

        // -------------------------------------------------------
        //  브리지 이벤트
        // -------------------------------------------------------

        private void HandleBoardReady(int[] itemIds, PlayerId firstPlayer)
        {
            // 재대국을 대비해 초기화한다.
            _hasPenalty = false;
            Close();
        }

        private void HandleTurnChanged(PlayerId player, GameController.Phase phase)
        {
            bool isMyDecision = phase == GameController.Phase.AnswerDecision
                                && player == bridge.LocalPlayerId;

            if (!isMyDecision)
            {
                _pendingOpen = false;
                Close();
                return;
            }

            // 알림이 재생 중이면 끝난 뒤에 연다.
            if (feedback != null && feedback.IsBusy)
            {
                _pendingOpen = true;
                Close();
                return;
            }

            Open();
        }

        private void HandleFeedbackFinished()
        {
            if (!_pendingOpen) return;

            _pendingOpen = false;
            Open();
        }

        private void HandlePenaltyChanged(PlayerId player, bool has)
        {
            if (player != bridge.LocalPlayerId) return;

            _hasPenalty = has;
            RefreshButtons();
        }

        private void HandleGameFinished(GameResult result)
        {
            Close();
        }

        // -------------------------------------------------------
        //  열고 닫기
        // -------------------------------------------------------

        private void Open()
        {
            Panel.SetActive(true);

            if (messageText != null)
            {
                messageText.text = "Guess or Pass";
            }

            RefreshButtons();
        }

        private void Close()
        {
            Panel.SetActive(false);
        }

        private void RefreshButtons()
        {
            // 토큰 보유 중에는 추측할 수 없다. 버튼만 막고 이유를 함께 보여준다.
            // (안내가 없으면 눌러도 반응이 없어 고장난 것처럼 보인다)
            if (guessButton != null) guessButton.interactable = !_hasPenalty;

            if (penaltyHintText != null)
            {
                penaltyHintText.gameObject.SetActive(_hasPenalty);
                penaltyHintText.text = "You have a penalty token. You can't guess this turn.";
            }
        }

        // -------------------------------------------------------
        //  버튼
        // -------------------------------------------------------

        private void OnGuessClicked()
        {
            if (_hasPenalty) return;

            bridge.RequestAnswerDecision(true);
            Close();
        }

        private void OnPassClicked()
        {
            bridge.RequestAnswerDecision(false);
            Close();
        }
    }
}
