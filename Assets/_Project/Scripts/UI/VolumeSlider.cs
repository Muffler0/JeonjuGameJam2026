using Project.Core.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 볼륨 슬라이더 하나에 붙이는 컴포넌트.
    /// Slider와 같은 오브젝트에 붙이고, 인스펙터에서 어느 채널인지만 고르면 된다.
    /// </summary>
    [RequireComponent(typeof(Slider))]
    public class VolumeSlider : MonoBehaviour
    {
        public enum Channel
        {
            Master,
            Bgm,
            Sfx
        }

        [Tooltip("Channel to be adjusted by slider (Master, Bgm, Sfx)")]
        [SerializeField] private Channel channel = Channel.Master;

        [Tooltip("Text to display the current value as a number. If not, you can leave it blank.")]
        [SerializeField] private TMP_Text valueText;

        // 처음 실행했을 때의 기본 볼륨
        private const float DefaultVolume = 0.8f;

        private Slider _slider;

        private string PrefsKey => $"volume_{channel}";

        private void Awake()
        {
            _slider = GetComponent<Slider>();
            _slider.minValue = 0f;
            _slider.maxValue = 1f;
            _slider.wholeNumbers = false;

            float saved = PlayerPrefs.GetFloat(PrefsKey, DefaultVolume);

            // SetValueWithoutNotify를 쓰지 않으면 초기화 중에 onValueChanged가 불려
            // 저장 로직이 한 번 더 돌아간다.
            // Code by Claude Opus
            _slider.SetValueWithoutNotify(saved);

            ApplyToSoundManager(saved);
            UpdateText(saved);

            _slider.onValueChanged.AddListener(OnValueChanged);
        }

        private void OnValueChanged(float value)
        {
            ApplyToSoundManager(value);
            UpdateText(value);

            PlayerPrefs.SetFloat(PrefsKey, value);
        }

        private void ApplyToSoundManager(float value)
        {
            switch (channel)
            {
                case Channel.Master:
                    SoundManager.Instance.SetMasterVolume(value);
                    break;
                case Channel.Bgm:
                    SoundManager.Instance.SetBgmVolume(value);
                    break;
                case Channel.Sfx:
                    SoundManager.Instance.SetSfxVolume(value);
                    break;
            }
        }

        private void UpdateText(float value)
        {
            if (valueText == null) return;
            valueText.text = Mathf.RoundToInt(value * 100f).ToString();
        }

        /// <summary>
        /// 설정 패널을 닫을 때 저장한다.
        /// </summary>
        private void OnDisable()
        {
            PlayerPrefs.Save();
        }

        private void OnDestroy()
        {
            if (_slider != null) _slider.onValueChanged.RemoveListener(OnValueChanged);
        }
    }
}
