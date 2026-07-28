using System.Collections;
using DG.Tweening;
using Project.Core.Audio;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Core.SceneFlow
{
    /// <summary>
    /// 페이드 전환을 포함한 씬 이동을 담당하는 싱글톤.
    ///
    /// --- 사용법
    /// SceneLoader.Instance.Load(SceneName.Game);
    /// SceneLoader.Instance.Reload();
    /// SceneLoader.Instance.LoadNext();
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneLoader : MonoBehaviour
    {
        // --- 전환 연출 설정 (여기 숫자만 바꾸면 전체 톤이 바뀐다) ---
        private const float DefaultFadeOutDuration = 0.35f;
        private const float DefaultFadeInDuration = 0.35f;
        private const Ease FadeEase = Ease.InOutQuad;

        // 다른 UI보다 항상 위에 그려지도록 큰 값을 준다.
        private const int FadeCanvasSortingOrder = 30000;

        private static readonly Color FadeColor = Color.black;

        // -------------------------------
        //  싱글톤
        // -------------------------------
        private static SceneLoader _instance;
        public static SceneLoader Instance
        {
            get
            {
                if (_instance == null) CreateInstance();
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            CreateInstance();
        }

        private static void CreateInstance()
        {
            if (_instance != null) return;

            _instance = FindAnyObjectByType<SceneLoader>();
            if (_instance != null) return;

            var go = new GameObject("[SceneLoader]");
            _instance = go.AddComponent<SceneLoader>();
        }

        private Image _fadeImage;
        private bool _isTransitioning;

        /// <summary>
        /// 전환이 진행 중인지 여부. 필요하면 외부에서 읽어 쓸 수 있다.
        /// </summary>
        public bool IsTransitioning => _isTransitioning;

        private void Awake()
        {
            // 싱글톤
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            CreateFadeCanvas();
        }

        /// <summary>
        /// 화면 전체를 덮는 페이드용 캔버스를 생성한다.
        /// </summary>
        private void CreateFadeCanvas()
        {
            var canvasGo = new GameObject("FadeCanvas");
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = FadeCanvasSortingOrder;

            // 페이드 중 UI 클릭을 막기 위해 필요하다. (아래 raycastTarget과 한 쌍)
            canvasGo.AddComponent<GraphicRaycaster>();

            var imageGo = new GameObject("FadeImage");
            imageGo.transform.SetParent(canvasGo.transform, false);

            _fadeImage = imageGo.AddComponent<Image>();
            _fadeImage.color = new Color(FadeColor.r, FadeColor.g, FadeColor.b, 0f);
            _fadeImage.raycastTarget = false;

            var rect = _fadeImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 지정한 씬으로 이동한다.
        /// </summary>
        public void Load(string sceneName,
                         bool fadeOutBgm = false,
                         float fadeOutDuration = DefaultFadeOutDuration,
                         float fadeInDuration = DefaultFadeInDuration)
        {
            // 전환 중 버튼을 두 번 눌러 씬이 두 번 로드되는 사고를 막는다.
            if (_isTransitioning)
            {
                Debug.Log($"[SceneLoader] 이미 전환 중이라 '{sceneName}' 요청을 무시했습니다.");
                return;
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("[SceneLoader] 씬 이름이 비어 있습니다.");
                return;
            }

            // 여기서 미리 걸러야 원인이 명확한 에러를 띄울 수 있다.
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneLoader] '{sceneName}' 씬을 불러올 수 없습니다.\n" +
                               $"→ 씬 이름 오타이거나, File > Build Profiles 의 Scene List 에 등록되지 않았습니다.");
                return;
            }

            StartCoroutine(TransitionRoutine(sceneName, fadeOutBgm, fadeOutDuration, fadeInDuration));
        }

        /// <summary>
        /// 현재 씬을 처음부터 다시 시작한다.=
        /// </summary>
        public void Reload(bool fadeOutBgm = false,
                           float fadeOutDuration = DefaultFadeOutDuration,
                           float fadeInDuration = DefaultFadeInDuration)
        {
            Load(SceneManager.GetActiveScene().name, fadeOutBgm, fadeOutDuration, fadeInDuration);
        }

        /// <summary>
        /// Build Profiles 의 Scene List 순서상 다음 씬으로 이동한다.
        /// </summary>
        public void LoadNext(bool fadeOutBgm = false,
                             float fadeOutDuration = DefaultFadeOutDuration,
                             float fadeInDuration = DefaultFadeInDuration)
        {
            int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;

            if (nextIndex >= SceneManager.sceneCountInBuildSettings)
            {
                Debug.LogWarning("[SceneLoader] 다음 씬이 없습니다. Scene List의 마지막 씬입니다.");
                return;
            }

            string path = SceneUtility.GetScenePathByBuildIndex(nextIndex);
            string nextName = System.IO.Path.GetFileNameWithoutExtension(path);

            Load(nextName, fadeOutBgm, fadeOutDuration, fadeInDuration);
        }

        // -------------------------------
        //  전환 처리
        // -------------------------------
        private IEnumerator TransitionRoutine(string sceneName, bool fadeOutBgm,
                                              float fadeOutDuration, float fadeInDuration)
        {
            _isTransitioning = true;
            _fadeImage.raycastTarget = true;    // 전환 중 모든 UI 클릭 차단

            if (fadeOutBgm)
                SoundManager.Instance.StopBGM(fadeOutDuration);

            // --- 1. 화면 어둡게 ---
            var fadeOut = _fadeImage.DOFade(1f, fadeOutDuration)
                                    .SetEase(FadeEase)
                                    .SetUpdate(true)
                                    .SetLink(gameObject);

            // --- 2. 페이드와 씬 로딩 시작 ---
            var operation = SceneManager.LoadSceneAsync(sceneName);
            operation.allowSceneActivation = false;     // 페이드가 끝날 때까지 화면 교체를 미룬다

            yield return fadeOut.WaitForCompletion();

            // allowSceneActivation이 false면 진행률은 0.9에서 멈춘다.
            // Code by Claude Opus
            while (operation.progress < 0.9f)
                yield return null;

            // --- 3. 씬 교체 ---
            operation.allowSceneActivation = true;
            while (!operation.isDone)
                yield return null;

            // 일시정지 상태에서 씬을 넘어온 경우 시간이 멈춘 채로 남는다.
            Time.timeScale = 1f;

            // --- 4. 화면 밝게 ---
            yield return _fadeImage.DOFade(0f, fadeInDuration)
                                   .SetEase(FadeEase)
                                   .SetUpdate(true)
                                   .SetLink(gameObject)
                                   .WaitForCompletion();

            _fadeImage.raycastTarget = false;
            _isTransitioning = false;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
