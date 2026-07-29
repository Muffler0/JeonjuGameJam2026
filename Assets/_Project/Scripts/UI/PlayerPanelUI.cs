using Project.Core.Network;
using Project.Data;
using Project.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 좌우 하단의 플레이어 초상화 패널.
    ///
    /// 좌우가 같은 구조라 컴포넌트는 하나만 만들고,
    /// 인스펙터의 Is Local Player로 어느 쪽인지만 구분한다.
    /// </summary>
    public class PlayerPanelUI : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private NetworkBridge bridge;
        [SerializeField] private GameBoardUI board;
        [SerializeField] private ToyLibrary toyLibrary;

        [Tooltip("체크하면 내 패널(왼쪽), 해제하면 상대 패널(오른쪽)")]
        [SerializeField] private bool isLocalPlayer = true;

        [Header("표시 요소")]
        [SerializeField] private Image portrait;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private GameObject speakingMark;
        [SerializeField] private GameObject penaltyMark;

        [Header("내 장난감 (상대 패널에서는 비워둘 것)")]
        [SerializeField] private GameObject myToySlot;
        [SerializeField] private Image myToyIcon;

        [Header("말풍선")]
        [Tooltip("말풍선이 생길 기준 위치")]
        [SerializeField] private RectTransform bubbleAnchor;

        /// <summary>채팅 말풍선이 이 위치에서 생성된다.</summary>
        public RectTransform BubbleAnchor { get { return bubbleAnchor; } }

        /// <summary>이 패널이 나타내는 플레이어. 접속 전에는 None.</summary>
        public PlayerId Owner
        {
            get
            {
                if (bridge == null) return PlayerId.None;
                return isLocalPlayer ? bridge.LocalPlayerId : bridge.OpponentPlayerId;
            }
        }

        // -------------------------------------------------------
        //  초기화
        // -------------------------------------------------------

        private void Awake()
        {
            if (nameText != null) nameText.text = isLocalPlayer ? "You" : "Opponent";

            SetSpeaking(false);
            SetPenalty(false);
            HideMyToy();
        }

        private void OnEnable()
        {
            if (bridge != null)
            {
                bridge.OnBoardReady += HandleBoardReady;
                bridge.OnPenaltyChanged += HandlePenaltyChanged;
                bridge.OnGameFinished += HandleGameFinished;
            }

            if (board != null)
            {
                board.OnMySecretChosen += HandleMySecretChosen;
            }
        }

        private void OnDisable()
        {
            if (bridge != null)
            {
                bridge.OnBoardReady -= HandleBoardReady;
                bridge.OnPenaltyChanged -= HandlePenaltyChanged;
                bridge.OnGameFinished -= HandleGameFinished;
            }

            if (board != null)
            {
                board.OnMySecretChosen -= HandleMySecretChosen;
            }
        }

        // -------------------------------------------------------
        //  브리지 이벤트
        // -------------------------------------------------------

        private void HandleBoardReady(int[] itemIds, PlayerId firstPlayer)
        {
            // 재대국에 대비해 매 판 초기화한다.
            SetPenalty(false);
            SetSpeaking(false);
            HideMyToy();
        }

        private void HandlePenaltyChanged(PlayerId player, bool has)
        {
            if (player != Owner) return;
            SetPenalty(has);
        }

        private void HandleGameFinished(GameResult result)
        {
            SetSpeaking(false);
        }

        private void HandleMySecretChosen(int itemId)
        {
            // 자기 장난감은 자기 패널에만 표시한다.
            if (!isLocalPlayer) return;

            if (myToySlot != null) myToySlot.SetActive(true);

            if (myToyIcon != null)
            {
                var sprite = toyLibrary != null ? toyLibrary.GetSprite(itemId) : null;
                myToyIcon.sprite = sprite;
                myToyIcon.preserveAspect = true;
                myToyIcon.enabled = sprite != null;
            }
        }

        // -------------------------------------------------------
        //  표시 제어
        // -------------------------------------------------------

        /// <summary>음성 채팅에서 말하는 중 표시. 음성 기능이 붙으면 여기로 연결한다.</summary>
        public void SetSpeaking(bool speaking)
        {
            if (speakingMark != null) speakingMark.SetActive(speaking);
        }

        /// <summary>초상화 이미지를 바꾼다. 캐릭터 선택이 생기면 여기로 연결한다.</summary>
        public void SetPortrait(Sprite sprite)
        {
            if (portrait == null) return;

            portrait.sprite = sprite;
            portrait.preserveAspect = true;
        }

        private void SetPenalty(bool has)
        {
            if (penaltyMark != null) penaltyMark.SetActive(has);
        }

        private void HideMyToy()
        {
            if (myToySlot != null) myToySlot.SetActive(false);
        }
    }
}
