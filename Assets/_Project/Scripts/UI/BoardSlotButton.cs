using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 盤面の 1 スロット。BoardRoot の子として 24 個並べて使う。
    /// 自分のスロット番号を覚えておき、押されたら番号を返すだけ。
    ///
    /// icon にアイテム画像、label に名前（任意）を表示する。
    /// 画像だけで見せたい場合は label を空のままにしてよい。
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class BoardSlotButton : MonoBehaviour
    {
        [Header("表示")]
        [Tooltip("アイテム画像を表示する Image。")]
        [SerializeField] private Image icon;

        [Tooltip("アイテム名を出したい場合だけ設定する。不要なら空でよい。")]
        [SerializeField] private TMP_Text label;

        [Tooltip("ハイライトの色を変える枠。icon とは別の Image を指定する。")]
        [SerializeField] private Image frame;

        [Header("ハイライト色")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.4f);

        private Button _button;
        private Action<int> _onClicked;

        public int SlotIndex { get; private set; } = -1;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(HandleClick);
        }

        public void Setup(int slotIndex, string displayName, Sprite sprite, Action<int> onClicked)
        {
            SlotIndex = slotIndex;
            _onClicked = onClicked;

            if (label != null) label.text = displayName;

            if (icon != null)
            {
                icon.sprite = sprite;

                // 画像が未設定のときに白い四角が残らないようにする。
                icon.enabled = sprite != null;
            }

            SetHighlight(false);
        }

        public void SetInteractable(bool value)
        {
            if (_button == null) _button = GetComponent<Button>();
            _button.interactable = value;
        }

        public void SetHighlight(bool value)
        {
            if (frame == null) return;
            frame.color = value ? selectedColor : normalColor;
        }

        private void HandleClick()
        {
            _onClicked?.Invoke(SlotIndex);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(HandleClick);
        }
    }
}
