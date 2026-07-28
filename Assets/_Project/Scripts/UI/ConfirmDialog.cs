using Project.Core.Network;
using Project.Data;
using Project.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 보드에서 장난감을 고른 뒤 뜨는 확인 다이얼로그.
    ///
    /// 일반 턴에서는 확정하면 바로 닫히지만,
    /// 추가 턴에서는 상대를 기다려야 하므로 대기 표시로 바뀐 채 남아 있는다.
    /// (닫아버리면 자신이 무엇을 골랐는지 확인할 방법이 없어진다)
    /// </summary>
    public class ConfirmDialog : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private NetworkBridge bridge;
        [SerializeField] private GameBoardUI board;
        [SerializeField] private ToyLibrary toyLibrary;

        [Tooltip("열고 닫을 대상. 비워두면 이 오브젝트를 사용한다.")]
        [SerializeField] private GameObject panel;

        [Header("표시 요소")]
        [SerializeField] private Image selectedIcon;
        [SerializeField] private TMP_Text selectedNameText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;

        [Tooltip("추가 턴에서 확정한 뒤 표시할 대기 안내")]
        [SerializeField] private GameObject waitingGroup;

        private bool _isExtraTurn;
        private bool _submitted;

        private GameObject Panel { get { return panel != null ? panel : gameObject; } }

        // -------------------------------------------------------
        //  초기화
        // -------------------------------------------------------

        private void Awake()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);

            Close();
        }

        private void OnEnable()
        {
            if (board != null) board.OnCandidateSelected += HandleCandidateSelected;

            if (bridge == null) return;

            bridge.OnTurnStarted += HandleTurnStarted;
            bridge.OnTurnChanged += HandleTurnChanged;
            bridge.OnBoardReady += HandleBoardReady;
            bridge.OnGameFinished += HandleGameFinished;
        }

        private void OnDisable()
        {
            if (board != null) board.OnCandidateSelected -= HandleCandidateSelected;

            if (bridge == null) return;

            bridge.OnTurnStarted -= HandleTurnStarted;
            bridge.OnTurnChanged -= HandleTurnChanged;
            bridge.OnBoardReady -= HandleBoardReady;
            bridge.OnGameFinished -= HandleGameFinished;
        }

        private void OnDestroy()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(OnConfirmClicked);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(OnCancelClicked);
        }

        // -------------------------------------------------------
        //  이벤트
        // -------------------------------------------------------

        private void HandleBoardReady(int[] itemIds, PlayerId firstPlayer)
        {
            _isExtraTurn = false;
            _submitted = false;
            Close();
        }

        private void HandleTurnStarted(TurnInfo info)
        {
            _isExtraTurn = info.IsExtraTurn;
            _submitted = false;

            // 새 턴이 시작되면 이전 턴의 잔상을 남기지 않는다.
            Close();
        }

        private void HandleTurnChanged(PlayerId player, GameController.Phase phase)
        {
            // 확인 단계를 벗어나면 닫는다. 추가 턴은 phase가 그대로라 여기서 걸리지 않는다.
            if (_isExtraTurn) return;

            if (phase != GameController.Phase.Confirmation) Close();
        }

        private void HandleGameFinished(GameResult result)
        {
            Close();
        }

        private void HandleCandidateSelected(int slotIndex, int itemId)
        {
            _submitted = false;
            Open(itemId);
        }

        // -------------------------------------------------------
        //  열고 닫기
        // -------------------------------------------------------

        private void Open(int itemId)
        {
            Panel.SetActive(true);

            if (selectedIcon != null)
            {
                var sprite = toyLibrary != null ? toyLibrary.GetSprite(itemId) : null;
                selectedIcon.sprite = sprite;
                selectedIcon.preserveAspect = true;
                selectedIcon.enabled = sprite != null;
            }

            if (selectedNameText != null)
            {
                selectedNameText.text = toyLibrary != null ? toyLibrary.GetName(itemId) : string.Empty;
            }

            if (messageText != null)
            {
                messageText.text = _isExtraTurn
                    ? "Final answer. You can't change it later."
                    : "Is this the opponent's toy?";
            }

            SetWaiting(false);
        }

        private void Close()
        {
            Panel.SetActive(false);
            SetWaiting(false);
        }

        /// <summary>추가 턴에서 확정한 뒤 상대를 기다리는 표시로 바꾼다.</summary>
        private void SetWaiting(bool waiting)
        {
            if (confirmButton != null) confirmButton.gameObject.SetActive(!waiting);
            if (cancelButton != null) cancelButton.gameObject.SetActive(!waiting);
            if (waitingGroup != null) waitingGroup.SetActive(waiting);

            if (waiting && messageText != null)
            {
                messageText.text = "Waiting for the opponent...";
            }
        }

        // -------------------------------------------------------
        //  버튼
        // -------------------------------------------------------

        private void OnConfirmClicked()
        {
            if (_submitted) return;
            _submitted = true;

            board.ConfirmCandidate();

            if (_isExtraTurn)
            {
                // 상대가 확정할 때까지 결과가 나오지 않으므로 대기 상태로 남긴다.
                SetWaiting(true);
                return;
            }

            Close();
        }

        private void OnCancelClicked()
        {
            board.CancelCandidate();
            Close();
        }
    }
}
