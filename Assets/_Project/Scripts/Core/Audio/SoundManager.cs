using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Core.Audio
{
    /// <summary>
    /// 게임 전체의 사운드를 담당하는 싱글톤.
    ///     SoundManager.Instance.PlaySFX(SoundKey.UiClick);
    ///     SoundManager.Instance.PlayBGM(SoundKey.StageBgm);
    ///     SoundManager.Instance.StopBGM();
    ///
    /// 씬에 오브젝트를 배치하지 않아도 게임 시작 시 자동으로 생성된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class SoundManager : MonoBehaviour
    {
        // Resources 폴더 기준 SoundLibrary 에셋 경로
        private const string LibraryResourcePath = "SoundLibrary";

        // 동시에 재생 가능한 효과음 개수.
        private const int SfxSourceCount = 10;

        // ─────────────────────────────────────────────
        //  싱글톤
        // ─────────────────────────────────────────────

        private static SoundManager _instance;

        public static SoundManager Instance
        {
            get
            {
                if (_instance == null) CreateInstance();
                return _instance;
            }
        }

        /// <summary>
        /// 씬이 로드되기 전에 자동으로 호출된다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            CreateInstance();
        }

        private static void CreateInstance()
        {
            if (_instance != null) return;

            // 씬에 직접 배치해 둔 것이 있으면 그것을 우선 사용한다.
            _instance = FindAnyObjectByType<SoundManager>();
            if (_instance != null) return;

            var go = new GameObject("[SoundManager]");
            _instance = go.AddComponent<SoundManager>(); // AddComponent 시점에 Awake가 실행된다
        }

        // ─────────────────────────────────────────────
        //  필드
        // ─────────────────────────────────────────────

        [Tooltip("비워두면 Resources 폴더에서 자동으로 불러온다.")]
        [SerializeField] private SoundLibrary library;

        // 키 → 등록 정보. 매 재생마다 리스트를 훑지 않기 위해 딕셔너리로 변환해 둔다.
        private readonly Dictionary<string, SoundLibrary.SoundEntry> _bgmTable = new();
        private readonly Dictionary<string, SoundLibrary.SoundEntry> _sfxTable = new();

        // BGM용 AudioSource 2개. 크로스페이드를 위해 번갈아 사용한다.
        private readonly AudioSource[] _bgmSources = new AudioSource[2];
        private int _activeBgmIndex;

        // 효과음용 AudioSource 풀.
        private readonly List<AudioSource> _sfxSources = new();
        private string[] _sfxPlayingKeys;   // 각 소스가 현재 무슨 키를 재생 중인지 (개별 정지용)
        private int _nextSfxIndex;          // 전부 사용 중일 때 재사용할 순번

        private float _masterVolume = 1f;
        private float _bgmVolume = 1f;
        private float _sfxVolume = 1f;

        private SoundLibrary.SoundEntry _currentBgmEntry;
        private Coroutine _bgmFadeRoutine;

        private bool _initialized;

        // 현재 BGM이 최종적으로 가져야 할 볼륨.
        private float CurrentBgmTargetVolume =>
            _masterVolume * _bgmVolume * (_currentBgmEntry?.volume ?? 1f);

        // ─────────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────────

        private void Awake()
        {
            // 싱글톤 구조로 설계
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (library == null)
                library = Resources.Load<SoundLibrary>(LibraryResourcePath);

            if (library == null)
            {
                Debug.LogError($"[SoundManager] SoundLibrary를 찾지 못했습니다. " +
                               $"Resources/{LibraryResourcePath}.asset 이 있는지 확인하세요.");
            }
            else
            {
                BuildTable(library.bgmList, _bgmTable, "BGM");
                BuildTable(library.sfxList, _sfxTable, "SFX");
            }

            CreateAudioSources();
        }

        /// <summary>
        /// 리스트를 딕셔너리로 변환한다. 이때 키 누락과 중복을 함께 검사한다.
        /// </summary>
        private void BuildTable(List<SoundLibrary.SoundEntry> source,
                                Dictionary<string, SoundLibrary.SoundEntry> table,
                                string label)
        {
            if (source == null) return;

            foreach (var entry in source)
            {
                if (entry == null || entry.clip == null) continue;

                if (string.IsNullOrWhiteSpace(entry.key))
                {
                    Debug.LogWarning($"[SoundManager] {label} 목록에 키가 비어 있는 항목이 있습니다: {entry.clip.name}");
                    continue;
                }

                if (table.ContainsKey(entry.key))
                {
                    Debug.LogWarning($"[SoundManager] {label} 키가 중복됩니다: '{entry.key}'. 먼저 등록된 것만 사용됩니다.");
                    continue;
                }

                table.Add(entry.key, entry);
            }
        }

        private void CreateAudioSources()
        {
            for (int i = 0; i < _bgmSources.Length; i++)
            {
                _bgmSources[i] = CreateSource($"BGM_{i}", loop: true);
            }

            _sfxPlayingKeys = new string[SfxSourceCount];
            for (int i = 0; i < SfxSourceCount; i++)
            {
                _sfxSources.Add(CreateSource($"SFX_{i}", loop: false));
            }
        }

        private AudioSource CreateSource(string sourceName, bool loop)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform);

            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;   // 2D 게임이므로 공간 사운드를 끈다
            source.volume = 0f;
            return source;
        }

        // ─────────────────────────────────────────────
        //  BGM
        // ─────────────────────────────────────────────

        /// <summary>
        /// 배경음을 재생한다. 이미 같은 곡이 재생 중이면 아무 일도 하지 않는다.
        /// </summary>
        /// <param name="key">SoundKey 상수를 사용할 것</param>
        /// <param name="fadeDuration">전환에 걸리는 시간(초). 0이면 즉시 전환된다.</param>
        public void PlayBGM(string key, float fadeDuration = 0.8f)
        {
            if (!_bgmTable.TryGetValue(key, out var entry))
            {
                Debug.LogWarning($"[SoundManager] 등록되지 않은 BGM 키입니다: '{key}'");
                return;
            }

            var active = _bgmSources[_activeBgmIndex];

            // 같은 곡이 이미 재생 중이면 처음부터 다시 틀지 않는다.
            if (active.isPlaying && active.clip == entry.clip) return;

            int nextIndex = 1 - _activeBgmIndex;
            var next = _bgmSources[nextIndex];

            next.clip = entry.clip;
            next.pitch = 1f;
            next.volume = 0f;
            next.Play();

            _currentBgmEntry = entry;
            _activeBgmIndex = nextIndex;

            StartBgmFade(FadeRoutine(next, CurrentBgmTargetVolume, active, 0f, fadeDuration, stopFrom: true));
        }

        /// <summary>
        /// 배경음을 정지한다.
        /// </summary>
        public void StopBGM(float fadeDuration = 0.5f)
        {
            var active = _bgmSources[_activeBgmIndex];
            if (!active.isPlaying) return;

            _currentBgmEntry = null;
            StartBgmFade(FadeRoutine(null, 0f, active, 0f, fadeDuration, stopFrom: true));
        }

        public void PauseBGM() => _bgmSources[_activeBgmIndex].Pause();

        public void ResumeBGM() => _bgmSources[_activeBgmIndex].UnPause();

        private void StartBgmFade(IEnumerator routine)
        {
            if (_bgmFadeRoutine != null) StopCoroutine(_bgmFadeRoutine);
            _bgmFadeRoutine = StartCoroutine(routine);
        }

        /// <summary>
        /// to는 올리고 from은 내리는 크로스페이드. 둘 중 하나는 null이어도 된다.
        /// Time.timeScale이 0이어도 동작하도록 unscaledDeltaTime을 사용한다.
        /// (일시정지 메뉴에서 음량이 멈추는 것을 막기 위함)
        /// </summary>
        private IEnumerator FadeRoutine(AudioSource to, float toTarget,
                                        AudioSource from, float fromTarget,
                                        float duration, bool stopFrom)
        {
            float toStart = to != null ? to.volume : 0f;
            float fromStart = from != null ? from.volume : 0f;

            if (duration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);

                    if (to != null) to.volume = Mathf.Lerp(toStart, toTarget, t);
                    if (from != null && from != to) from.volume = Mathf.Lerp(fromStart, fromTarget, t);

                    yield return null;
                }
            }

            if (to != null) to.volume = toTarget;
            if (from != null && from != to)
            {
                from.volume = fromTarget;
                if (stopFrom)
                {
                    from.Stop();
                    from.clip = null;
                }
            }

            _bgmFadeRoutine = null;
        }

        // ─────────────────────────────────────────────
        //  SFX
        // ─────────────────────────────────────────────

        /// <summary>
        /// 효과음을 재생한다.
        /// </summary>
        /// <param name="key">SoundKey 상수를 사용할 것</param>
        /// <param name="volumeScale">이번 재생에만 적용할 볼륨 배율(0~1)</param>
        public void PlaySFX(string key, float volumeScale = 1f)
        {
            if (!_sfxTable.TryGetValue(key, out var entry))
            {
                Debug.LogWarning($"[SoundManager] 등록되지 않은 SFX 키입니다: '{key}'");
                return;
            }

            int index = GetAvailableSfxIndex();
            var source = _sfxSources[index];
            _sfxPlayingKeys[index] = key;

            source.clip = entry.clip;
            source.volume = _masterVolume * _sfxVolume * entry.volume * Mathf.Clamp01(volumeScale);

            // 피치를 살짝 흔들어 반복 재생이 기계적으로 들리는 것을 막는다.
            source.pitch = entry.pitchVariance > 0f
                ? 1f + Random.Range(-entry.pitchVariance, entry.pitchVariance)
                : 1f;

            source.Play();
        }

        /// <summary>
        /// 특정 키의 효과음만 즉시 정지한다. (긴 효과음을 중간에 끊어야 할 때)
        /// </summary>
        public void StopSFX(string key)
        {
            for (int i = 0; i < _sfxSources.Count; i++)
            {
                if (_sfxPlayingKeys[i] == key && _sfxSources[i].isPlaying)
                {
                    _sfxSources[i].Stop();
                    _sfxPlayingKeys[i] = null;
                }
            }
        }

        public void StopAllSFX()
        {
            for (int i = 0; i < _sfxSources.Count; i++)
            {
                _sfxSources[i].Stop();
                _sfxPlayingKeys[i] = null;
            }
        }

        /// <summary>
        /// 비어 있는 소스를 찾는다. 전부 사용 중이면 가장 오래전에 시작한 것을 재사용한다.
        /// </summary>
        private int GetAvailableSfxIndex()
        {
            for (int i = 0; i < _sfxSources.Count; i++)
            {
                if (!_sfxSources[i].isPlaying) return i;
            }

            int index = _nextSfxIndex;
            _nextSfxIndex = (_nextSfxIndex + 1) % _sfxSources.Count;
            return index;
        }

        // ─────────────────────────────────────────────
        //  볼륨
        // ─────────────────────────────────────────────

        public float MasterVolume => _masterVolume;
        public float BgmVolume => _bgmVolume;
        public float SfxVolume => _sfxVolume;

        /// <summary>전체 볼륨(0~1). 옵션 UI의 슬라이더에 그대로 연결하면 된다.</summary>
        public void SetMasterVolume(float value)
        {
            _masterVolume = Mathf.Clamp01(value);
            ApplyBgmVolume();
        }

        public void SetBgmVolume(float value)
        {
            _bgmVolume = Mathf.Clamp01(value);
            ApplyBgmVolume();
        }

        /// <summary>효과음 볼륨. 이미 재생 중인 소리에는 적용되지 않고 다음 재생부터 반영된다.</summary>
        public void SetSfxVolume(float value)
        {
            _sfxVolume = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 슬라이더를 움직이는 동안에는 페이드 코루틴이 볼륨을 덮어쓰므로,
        /// 페이드 중이 아닐 때만 즉시 반영한다.
        /// </summary>
        private void ApplyBgmVolume()
        {
            if (_bgmFadeRoutine != null) return;
            _bgmSources[_activeBgmIndex].volume = CurrentBgmTargetVolume;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
