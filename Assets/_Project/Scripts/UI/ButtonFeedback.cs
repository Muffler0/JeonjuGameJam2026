using DG.Tweening;
using Project.Core.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 소리를 내고 싶지 않은 버튼은 인스펙터에서 해당 키를 비워두면 된다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ButtonFeedback : MonoBehaviour,
        IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Header("사운드")]
        [Tooltip("클릭할 때 재생할 SFX 키.")]
        [SerializeField] private string clickSoundKey = SoundKey.UIClickSFX;

        [Tooltip("마우스를 올렸을 때 재생할 SFX 키.")]
        [SerializeField] private string hoverSoundKey = SoundKey.UIHoverSFX;

        [Tooltip("이 버튼의 소리 크기 배율. 호버음은 보통 클릭음보다 작게 둔다.")]
        [Range(0f, 1f)]
        [SerializeField] private float clickVolume = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float hoverVolume = 0.5f;

        // ----- 눌림 연출 -----
        [Header("눌림 연출")]
        [SerializeField] private bool usePressAnimation = true;

        [Tooltip("눌렀을 때 줄어들 비율")]
        [Range(0.8f, 1f)]
        [SerializeField] private float pressedScale = 0.95f;

        [Tooltip("크기가 변하는 데 걸리는 시간")]
        [SerializeField] private float animationDuration = 0.08f;

        private Button _button;

        // 디자인 단계에서 지정한 원래 크기. 1이라고 가정하지 않고 실제 값을 기억해 둔다.
        private Vector3 _baseScale;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _baseScale = transform.localScale;

            _button.onClick.AddListener(PlayClickSound);
        }

        // ----- 사운드 -----

        private void PlayClickSound()
        {
            PlaySound(clickSoundKey, clickVolume);
        }

        private void PlaySound(string key, float volume)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            SoundManager.Instance.PlaySFX(key, volume);
        }

        // ----- 포인터 이벤트 -----
        // Code by Claude Opus
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_button.interactable) return;

            PlaySound(hoverSoundKey, hoverVolume);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_button.interactable) return;
            AnimateScale(_baseScale * pressedScale);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            AnimateScale(_baseScale);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            AnimateScale(_baseScale);
        }

        // ----- 연출 -----
        private void AnimateScale(Vector3 target)
        {
            if (!usePressAnimation) return;

            // 이전 트윈이 남아 있으면 크기가 어중간한 값에서 멈춘다.
            transform.DOKill();

            transform.DOScale(target, animationDuration)
                     .SetEase(Ease.OutQuad)
                     .SetUpdate(true)
                     .SetLink(gameObject);
        }

        private void OnDisable()
        {
            // 패널이 닫히는 순간 줄어든 상태로 멈추는 것을 막는다.
            transform.DOKill();
            transform.localScale = _baseScale;
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(PlayClickSound);
        }
    }
}
