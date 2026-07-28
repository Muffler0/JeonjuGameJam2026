using Project.Core.SceneFlow;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.UI
{
    /// <summary>
    /// 타이틀 화면의 버튼 동작과 패널 열기/닫기를 담당한다.
    /// TitleCanvas 오브젝트에 붙이기
    /// </summary>
    public class TitleMenu : MonoBehaviour
    {
        [Header("Menu Buttons")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button creditsButton;
        [SerializeField] private Button quitButton;

        [Header("Panels")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject creditsPanel;

        [Header("Panel Close Buttons")]
        [SerializeField] private Button settingsCloseButton;
        [SerializeField] private Button creditsCloseButton;

        private void Awake()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (creditsPanel != null) creditsPanel.SetActive(false);

            // 아직 연결되지 않은 버튼이 있어도 에러 없이 넘어가도록 전부 null 검사를 한다.
            // (협동 방식이 정해지지 않아 버튼 하나가 비어 있는 상태를 허용하기 위함)
            AddListener(startButton, OnStartClicked);
            AddListener(settingsButton, OpenSettings);
            AddListener(creditsButton, OpenCredits);
            AddListener(quitButton, OnQuitClicked);

            AddListener(settingsCloseButton, CloseSettings);
            AddListener(creditsCloseButton, CloseCredits);
        }

        private void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.AddListener(action);
        }

        private void Update()
        {
            if (!EscapePressed()) return;

            if (creditsPanel != null && creditsPanel.activeSelf)
            {
                CloseCredits();
                return;
            }

            if (settingsPanel != null && settingsPanel.activeSelf)
            {
                CloseSettings();
            }
        }

        /// <summary>
        /// 프로젝트가 새 Input System을 쓰든 기존 Input Manager를 쓰든 동작하도록 분기한다.
        /// 게임 방향성이 안 정해져서...
        /// </summary>
        private static bool EscapePressed()
        {
            // code by Claude Opus
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        // -------------------------------
        //  버튼 동작
        // -------------------------------

        private void OnStartClicked()
        {
            // BGM도 화면과 함께 서서히 줄이며 게임 씬으로 이동한다.
            SceneLoader.Instance.Load(SceneName.Game, fadeOutBgm: true);
        }

        public void OpenSettings()
        {
            if (settingsPanel != null) settingsPanel.SetActive(true);
        }

        public void CloseSettings()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        public void OpenCredits()
        {
            if (creditsPanel != null) creditsPanel.SetActive(true);
        }

        public void CloseCredits()
        {
            if (creditsPanel != null) creditsPanel.SetActive(false);
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            // Code by Claude Opus
            RemoveListener(startButton);
            RemoveListener(settingsButton);
            RemoveListener(creditsButton);
            RemoveListener(quitButton);
            RemoveListener(settingsCloseButton);
            RemoveListener(creditsCloseButton);
        }

        private void RemoveListener(Button button)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
        }
    }
}
