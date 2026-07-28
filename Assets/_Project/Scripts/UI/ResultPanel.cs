using Project.Core.Network;
using Project.Core.SceneFlow;
using Project.Data;
using Project.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 게임이 끝났을 때 뜨는 화면.
    ///
    /// 정상 종료와 상대 이탈을 한 패널에서 처리한다.
    /// GameFinishedInfo.Reason으로 구분되므로 화면을 나눌 이유가 없고,
    /// 나누면 "어느 쪽을 띄울지" 판단하는 코드가 곳곳에 생긴다.
    /// </summary>
    public class ResultPanel : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private NetworkBridge bridge;
        [SerializeField] private ToyLibrary toyLibrary;

        [Tooltip("열고 닫을 대상. 비워두면 이 오브젝트를 사용한다.")]
        [SerializeField] private GameObject panel;

        [Header("결과 표시")]
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private TMP_Text statusText;

        [Header("정답 공개")]
        [SerializeField] private GameObject answerGroup;
        [SerializeField] private Image myToyIcon;
        [SerializeField] private TMP_Text myToyNameText;
        [SerializeField] private Image opponentToyIcon;
        [SerializeField] private TMP_Text opponentToyNameText;

        [Header("버튼")]
        [SerializeField] private Button rematchButton;
        [SerializeField] private Button exitButton;

        private GameObject Panel { get { return panel != null ? panel : gameObject; } }

        // -------------------------------------------------------
        //  초기화
        // -------------------------------------------------------

        private void Awake()
        {
            if (rematchButton != null) rematchButton.onClick.AddListener(OnRematchClicked);
            if (exitButton != null) exitButton.onClick.AddListener(OnExitClicked);

            Close();
        }

        private void OnEnable()
        {
            if (bridge == null) return;

            bridge.OnGameFinished += HandleGameFinished;
            bridge.OnOpponentMissing += HandleOpponentMissing;
            bridge.OnRematchVoteChanged += HandleRematchVoteChanged;
            bridge.OnRematchDeclined += HandleRematchDeclined;
            bridge.OnBoardReady += HandleBoardReady;
        }

        private void OnDisable()
        {
            if (bridge == null) return;

            bridge.OnGameFinished -= HandleGameFinished;
            bridge.OnOpponentMissing -= HandleOpponentMissing;
            bridge.OnRematchVoteChanged -= HandleRematchVoteChanged;
            bridge.OnRematchDeclined -= HandleRematchDeclined;
            bridge.OnBoardReady -= HandleBoardReady;
        }

        private void OnDestroy()
        {
            if (rematchButton != null) rematchButton.onClick.RemoveListener(OnRematchClicked);
            if (exitButton != null) exitButton.onClick.RemoveListener(OnExitClicked);
        }

        // -------------------------------------------------------
        //  브리지 이벤트
        // -------------------------------------------------------

        private void HandleGameFinished(GameResult result)
        {
            Panel.SetActive(true);

            ShowResultText(result.Info);
            ShowAnswers(result);

            bool canRematch = result.Info.Reason != GameEndReason.OpponentLeft;

            SetRematchAvailable(canRematch);
            SetStatus(canRematch ? "Play again?" : "The opponent left the game.");
        }

        private void HandleOpponentMissing()
        {
            // 씬 로딩 중에 상대가 나가 게임이 시작조차 못 한 경우.
            Panel.SetActive(true);

            if (resultText != null) resultText.text = "Game Canceled";
            if (answerGroup != null) answerGroup.SetActive(false);

            SetRematchAvailable(false);
            SetStatus("The opponent left before the game started.");
        }

        private void HandleRematchVoteChanged(int voted, int total)
        {
            SetStatus($"Rematch {voted} / {total}");
        }

        private void HandleRematchDeclined()
        {
            SetRematchAvailable(false);
            SetStatus("Rematch canceled.");
        }

        private void HandleBoardReady(int[] itemIds, PlayerId firstPlayer)
        {
            // 재대국이 시작됐다는 뜻이다.
            Close();
        }

        // -------------------------------------------------------
        //  표시
        // -------------------------------------------------------

        private void ShowResultText(GameFinishedInfo info)
        {
            if (resultText == null) return;

            if (info.Reason == GameEndReason.OpponentLeft)
            {
                resultText.text = "Game Canceled";
                return;
            }

            if (info.Reason == GameEndReason.Draw || info.Winner == PlayerId.None)
            {
                resultText.text = "Draw";
                return;
            }

            resultText.text = info.Winner == bridge.LocalPlayerId ? "You Win!" : "You Lose";
        }

        /// <summary>
        /// 양쪽의 정답 장난감을 공개한다.
        /// 클라이언트에는 룰 계층이 없으므로 브리지가 실어 보낸 값을 쓴다.
        /// </summary>
        private void ShowAnswers(GameResult result)
        {
            bool localIsPlayer1 = bridge.LocalPlayerId == PlayerId.Player1;

            int myItemId = localIsPlayer1 ? result.Player1SecretItemId : result.Player2SecretItemId;
            int opponentItemId = localIsPlayer1 ? result.Player2SecretItemId : result.Player1SecretItemId;

            // 시작 전에 끝난 경우 등 정답이 없으면 공개 영역 자체를 감춘다.
            bool hasAnswers = myItemId >= 0 && opponentItemId >= 0;

            if (answerGroup != null) answerGroup.SetActive(hasAnswers);
            if (!hasAnswers) return;

            ApplyToy(myToyIcon, myToyNameText, myItemId);
            ApplyToy(opponentToyIcon, opponentToyNameText, opponentItemId);
        }

        private void ApplyToy(Image icon, TMP_Text nameText, int itemId)
        {
            if (icon != null)
            {
                var sprite = toyLibrary != null ? toyLibrary.GetSprite(itemId) : null;
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.enabled = sprite != null;
            }

            if (nameText != null)
            {
                nameText.text = toyLibrary != null ? toyLibrary.GetName(itemId) : string.Empty;
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        private void SetRematchAvailable(bool available)
        {
            if (rematchButton != null) rematchButton.interactable = available;
        }

        private void Close()
        {
            Panel.SetActive(false);
        }

        // -------------------------------------------------------
        //  버튼
        // -------------------------------------------------------

        private void OnRematchClicked()
        {
            bridge.RequestRematch(true);

            // 양쪽이 모두 동의해야 시작하므로, 누른 뒤에는 기다리는 상태가 된다.
            SetRematchAvailable(false);
            SetStatus("Waiting for the opponent...");
        }

        private void OnExitClicked()
        {
            // 방을 떠나면 상대 쪽에서 이탈 처리가 돌아가므로 별도 통지는 필요 없다.
            bridge.LeaveGame();
            SceneLoader.Instance.Load(SceneName.Title, fadeOutBgm: true);
        }
    }
}
