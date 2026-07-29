using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Project.Core.Network;
using Project.Gameplay;
using TMPro;
using UnityEngine;

namespace Project.UI
{
    /// <summary>
    /// 화면 중앙에 진행 상황을 하나씩 띄운다.
    ///
    /// 판정, 페널티, 턴 시작이 한 프레임에 연달아 일어나기 때문에
    /// 그대로 두면 사람이 인지할 틈이 없다. 큐에 쌓아 순서대로 재생하고,
    /// 재생이 끝날 때까지 조작 패널이 열리지 않게 한다.
    /// </summary>
    public class GameFeedbackUI : MonoBehaviour
    {
        private struct Entry
        {
            public string Title;
            public string Sub;
            public Color Color;
            public bool Emphasize;
        }

        [Header("연결")]
        [SerializeField] private NetworkBridge bridge;

        [Tooltip("페이드에 쓸 CanvasGroup. 배너 내용물에 붙일 것")]
        [SerializeField] private CanvasGroup contentGroup;

        [SerializeField] private TMP_Text bannerText;
        [SerializeField] private TMP_Text subText;

        [Header("타이밍")]
        [SerializeField] private float fadeInDuration = 0.15f;
        [SerializeField] private float holdDuration = 0.9f;
        [SerializeField] private float fadeOutDuration = 0.25f;

        [Header("색")]
        [SerializeField] private Color normalColor = Color.white;

        [Tooltip("내 차례를 알릴 때")]
        [SerializeField] private Color emphasisColor = new Color(0.37f, 0.78f, 1f);

        [Tooltip("오답, 페널티 등")]
        [SerializeField] private Color warningColor = new Color(1f, 0.42f, 0.42f);

        // -------------------------------------------------------
        //  상태
        // -------------------------------------------------------

        private readonly Queue<Entry> _queue = new Queue<Entry>();
        private Coroutine _playRoutine;

        /// <summary>재생 중이거나 대기 중인 알림이 있는지.</summary>
        public bool IsBusy { get { return _playRoutine != null || _queue.Count > 0; } }

        /// <summary>큐가 모두 비었다. 조작 패널은 이 시점에 열면 된다.</summary>
        public event Action OnAllPlayed;

        private PlayerId Local { get { return bridge != null ? bridge.LocalPlayerId : PlayerId.None; } }

        // -------------------------------------------------------
        //  초기화
        // -------------------------------------------------------

        private void Awake()
        {
            if (contentGroup != null) contentGroup.alpha = 0f;
        }

        private void OnEnable()
        {
            if (bridge == null) return;

            bridge.OnPassed += HandlePassed;
            bridge.OnJudged += HandleJudged;
            bridge.OnPenaltyChanged += HandlePenaltyChanged;
            bridge.OnTurnStarted += HandleTurnStarted;
            bridge.OnGameFinished += HandleGameFinished;
        }

        private void OnDisable()
        {
            if (bridge == null) return;

            bridge.OnPassed -= HandlePassed;
            bridge.OnJudged -= HandleJudged;
            bridge.OnPenaltyChanged -= HandlePenaltyChanged;
            bridge.OnTurnStarted -= HandleTurnStarted;
            bridge.OnGameFinished -= HandleGameFinished;
        }

        // -------------------------------------------------------
        //  브리지 이벤트
        // -------------------------------------------------------

        private void HandlePassed(PlayerId player)
        {
            bool isMine = player == Local;

            Enqueue(new Entry
            {
                Title = isMine ? "You passed" : "Opponent passed",
                Sub = string.Empty,
                Color = normalColor,
                Emphasize = false
            });
        }

        private void HandleJudged(JudgeReport report)
        {
            // 정답이면 곧바로 결과 화면이 뜨므로 배너를 띄우지 않는다.
            if (report.Correct) return;

            bool isMine = report.Answerer == Local;

            Enqueue(new Entry
            {
                Title = "Wrong!",
                Sub = isMine ? "Your guess was wrong" : "Opponent guessed wrong",
                Color = warningColor,
                Emphasize = false
            });
        }

        private void HandlePenaltyChanged(PlayerId player, bool has)
        {
            // 토큰이 소진되는 순간은 알릴 필요가 없다. 획득만 알린다.
            if (!has) return;

            bool isMine = player == Local;

            Enqueue(new Entry
            {
                Title = "+1 Penalty Token",
                Sub = isMine ? "You can't guess while holding it" : "Opponent can't guess",
                Color = warningColor,
                Emphasize = false
            });
        }

        private void HandleTurnStarted(TurnInfo info)
        {
            if (info.IsExtraTurn)
            {
                Enqueue(new Entry
                {
                    Title = "Final Guess!",
                    Sub = "Both players guess now",
                    Color = emphasisColor,
                    Emphasize = true
                });
                return;
            }

            // 설명하는 쪽이 상대라면 이번 턴에 추측하는 건 나다.
            bool isMyGuessTurn = info.Describer != Local;

            Enqueue(new Entry
            {
                Title = $"Turn {info.TurnNumber} / {info.MaxTurnCount}",
                Sub = isMyGuessTurn ? "Your turn to guess" : "Describe your toy",
                Color = isMyGuessTurn ? emphasisColor : normalColor,
                Emphasize = isMyGuessTurn
            });
        }

        private void HandleGameFinished(GameResult result)
        {
            // 결과 화면이 주인공이므로 남은 알림은 버린다.
            _queue.Clear();

            if (_playRoutine != null)
            {
                StopCoroutine(_playRoutine);
                _playRoutine = null;
            }

            if (contentGroup != null)
            {
                contentGroup.DOKill();
                contentGroup.alpha = 0f;
            }
        }

        // -------------------------------------------------------
        //  재생
        // -------------------------------------------------------

        private void Enqueue(Entry entry)
        {
            _queue.Enqueue(entry);

            if (_playRoutine == null) _playRoutine = StartCoroutine(PlayLoop());
        }

        private IEnumerator PlayLoop()
        {
            while (_queue.Count > 0)
            {
                yield return Play(_queue.Dequeue());
            }

            _playRoutine = null;
            OnAllPlayed?.Invoke();
        }

        private IEnumerator Play(Entry entry)
        {
            if (bannerText != null)
            {
                bannerText.text = entry.Title;
                bannerText.color = entry.Color;
            }

            if (subText != null)
            {
                subText.text = entry.Sub;
                subText.color = entry.Color;
                subText.gameObject.SetActive(!string.IsNullOrEmpty(entry.Sub));
            }

            if (contentGroup == null) yield break;

            contentGroup.DOKill();
            contentGroup.alpha = 0f;

            var rect = contentGroup.transform as RectTransform;

            // 내 차례를 알릴 때만 살짝 커졌다 돌아오게 해서 눈에 띄게 한다.
            if (rect != null)
            {
                rect.DOKill();
                rect.localScale = Vector3.one * (entry.Emphasize ? 0.85f : 1f);

                if (entry.Emphasize)
                {
                    rect.DOScale(1f, fadeInDuration + 0.1f).SetEase(Ease.OutBack);
                }
            }

            yield return contentGroup.DOFade(1f, fadeInDuration).WaitForCompletion();
            yield return new WaitForSeconds(holdDuration);
            yield return contentGroup.DOFade(0f, fadeOutDuration).WaitForCompletion();
        }
    }
}
