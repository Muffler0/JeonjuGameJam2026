using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Project.Core.Network;
using Project.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 채팅 입력, 로그, 말풍선을 담당한다.
    ///
    /// 채팅은 룰 계층을 거치지 않고 브리지에서 바로 중계된다.
    /// 승패에 영향을 주지 않으므로 GameController가 알 필요가 없다.
    /// </summary>
    public class ChatUI : MonoBehaviour
    {
        private const int MaxLogEntries = 50;

        [Header("연결")]
        [SerializeField] private NetworkBridge bridge;

        [Header("입력")]
        [SerializeField] private TMP_InputField chatInput;
        [SerializeField] private Button sendButton;

        [Header("토글")]
        [SerializeField] private Button toggleButton;
        [SerializeField] private TMP_Text toggleLabel;
        [SerializeField] private GameObject newMessageMark;
        [SerializeField] private GameObject chatLogPanel;

        [Header("로그")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private TMP_Text logEntryPrefab;

        [Header("말풍선 (비워두면 말풍선 없이 동작)")]
        [SerializeField] private RectTransform bubbleLayer;
        [SerializeField] private GameObject bubblePrefab;
        [SerializeField] private PlayerPanelUI localPanel;
        [SerializeField] private PlayerPanelUI opponentPanel;

        [Header("연출 설정")]
        [SerializeField] private float bubbleRiseDistance = 60f;
        [SerializeField] private float bubbleLifetime = 2f;
        [SerializeField] private float bubbleFadeDuration = 0.6f;

        [Header("이름 색")]
        [SerializeField] private Color myNameColor = new Color(0.37f, 0.78f, 1f);
        [SerializeField] private Color opponentNameColor = new Color(1f, 0.72f, 0.4f);

        // -------------------------------------------------------
        //  상태
        // -------------------------------------------------------

        private readonly Queue<GameObject> _logEntries = new Queue<GameObject>();
        private Coroutine _scrollRoutine;

        // IME 확정과 Enter가 겹쳐 두 번 전송되는 것을 막는다.
        private float _lastSendTime;

        private bool IsLogOpen { get { return chatLogPanel != null && chatLogPanel.activeSelf; } }

        // -------------------------------------------------------
        //  초기화
        // -------------------------------------------------------

        private void Awake()
        {
            if (sendButton != null) sendButton.onClick.AddListener(Send);
            if (toggleButton != null) toggleButton.onClick.AddListener(ToggleLog);
            if (chatInput != null) chatInput.onSubmit.AddListener(_ => Send());

            if (chatLogPanel != null) chatLogPanel.SetActive(false);
            if (newMessageMark != null) newMessageMark.SetActive(false);

            RefreshToggleLabel();
        }

        private void OnEnable()
        {
            if (bridge == null) return;

            bridge.OnChatReceived += HandleChatReceived;
            bridge.OnBoardReady += HandleBoardReady;
        }

        private void OnDisable()
        {
            if (bridge == null) return;

            bridge.OnChatReceived -= HandleChatReceived;
            bridge.OnBoardReady -= HandleBoardReady;
        }

        private void OnDestroy()
        {
            if (sendButton != null) sendButton.onClick.RemoveListener(Send);
            if (toggleButton != null) toggleButton.onClick.RemoveListener(ToggleLog);
        }

        // -------------------------------------------------------
        //  전송
        // -------------------------------------------------------

        private void Send()
        {
            // 한글·일본어 입력에서 조합 확정과 Enter가 같은 프레임에 겹칠 수 있다.
            if (Time.unscaledTime - _lastSendTime < 0.15f) return;

            if (chatInput == null || string.IsNullOrWhiteSpace(chatInput.text)) return;

            _lastSendTime = Time.unscaledTime;
            bridge.SendChat(chatInput.text);

            chatInput.text = string.Empty;

            // 전송 후에도 계속 입력할 수 있도록 포커스를 되돌린다.
            chatInput.ActivateInputField();
        }

        // -------------------------------------------------------
        //  수신
        // -------------------------------------------------------

        private void HandleChatReceived(PlayerId sender, string message)
        {
            bool isMine = sender == bridge.LocalPlayerId;

            AppendLog(isMine, message);
            ShowBubble(isMine, message);

            // 로그가 접혀 있으면 놓칠 수 있으므로 표시를 남긴다.
            if (!isMine && !IsLogOpen && newMessageMark != null) newMessageMark.SetActive(true);    
        }

        private void HandleBoardReady(int[] itemIds, PlayerId firstPlayer)
        {
            // 재대국해도 대화는 이어지는 편이 자연스러워서 로그는 지우지 않는다.
        }

        // -------------------------------------------------------
        //  로그
        // -------------------------------------------------------

        private void AppendLog(bool isMine, string message)
        {
            if (logEntryPrefab == null || contentRoot == null) return;

            var entry = Instantiate(logEntryPrefab, contentRoot);

            string name = isMine ? "You" : "Opponent";
            string colorHex = ColorUtility.ToHtmlStringRGB(isMine ? myNameColor : opponentNameColor);

            entry.text = $"<color=#{colorHex}><b>{name}</b></color>: {message}";

            _logEntries.Enqueue(entry.gameObject);

            // 무한히 쌓이면 오브젝트가 계속 늘어난다.
            while (_logEntries.Count > MaxLogEntries)
            {
                var oldest = _logEntries.Dequeue();
                if (oldest != null) Destroy(oldest);
            }

            if (_scrollRoutine != null) StopCoroutine(_scrollRoutine);
            _scrollRoutine = StartCoroutine(ScrollToBottom());
        }

        /// <summary>
        /// 레이아웃이 갱신된 뒤에야 정확한 위치로 갈 수 있어 한 프레임 기다린다.
        /// </summary>
        private IEnumerator ScrollToBottom()
        {
            yield return null;

            Canvas.ForceUpdateCanvases();

            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;

            _scrollRoutine = null;
        }

        // -------------------------------------------------------
        //  토글
        // -------------------------------------------------------

        private void ToggleLog()
        {
            if (chatLogPanel == null) return;

            bool open = !chatLogPanel.activeSelf;
            chatLogPanel.SetActive(open);

            if (open)
            {
                if (newMessageMark != null) newMessageMark.SetActive(false);

                if (_scrollRoutine != null) StopCoroutine(_scrollRoutine);
                _scrollRoutine = StartCoroutine(ScrollToBottom());
            }

            RefreshToggleLabel();
        }

        private void RefreshToggleLabel()
        {
            if (toggleLabel == null) return;

            // 펼쳐져 있으면 "내려서 접는다", 접혀 있으면 "올려서 펼친다".
            toggleLabel.text = IsLogOpen ? "▼" : "▲";
        }

        // -------------------------------------------------------
        //  말풍선
        // -------------------------------------------------------

        private void ShowBubble(bool isMine, string message)
        {
            if (bubblePrefab == null || bubbleLayer == null) return;

            var panel = isMine ? localPanel : opponentPanel;
            if (panel == null || panel.BubbleAnchor == null) return;

            var bubble = Instantiate(bubblePrefab, bubbleLayer);
            var rect = bubble.GetComponent<RectTransform>();
            if (rect == null) return;

            var text = bubble.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = message;

            // 초상화 위 기준점에 맞춘다.
            rect.position = panel.BubbleAnchor.position;

            var group = bubble.GetComponent<CanvasGroup>();
            if (group == null) group = bubble.AddComponent<CanvasGroup>();

            PlayBubbleAnimation(bubble, rect, group);
        }

        private void PlayBubbleAnimation(GameObject bubble, RectTransform rect, CanvasGroup group)
        {
            rect.localScale = Vector3.one * 0.8f;
            group.alpha = 1f;

            float targetY = rect.anchoredPosition.y + bubbleRiseDistance;

            var sequence = DOTween.Sequence().SetLink(bubble);

            // 뿅 하고 나타난 뒤 천천히 올라가며 사라진다.
            sequence.Append(rect.DOScale(1f, 0.2f).SetEase(Ease.OutBack));
            sequence.Join(rect.DOAnchorPosY(targetY, bubbleLifetime).SetEase(Ease.OutQuad));
            sequence.Insert(bubbleLifetime - bubbleFadeDuration,
                            group.DOFade(0f, bubbleFadeDuration));

            sequence.OnComplete(() => Destroy(bubble));
        }
    }
}
