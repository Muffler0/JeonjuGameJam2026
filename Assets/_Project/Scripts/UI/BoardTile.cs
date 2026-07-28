using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// 보드 타일 하나. 게임 규칙은 전혀 모르고,
    /// "눌렸다"고 알리는 것과 지시받은 표시 상태로 바뀌는 것만 한다.
    ///
    /// 위치는 씬에서 자유롭게 옮겨도 되며, 슬롯 번호는 인스펙터에서 직접 지정한다.
    /// 하이어라키 순서에 의존하지 않으므로 정렬을 바꿔도 안전하다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class BoardTile : MonoBehaviour
    {
        public enum TileState
        {
            /// <summary>기본. 클릭 가능</summary>
            Normal,

            /// <summary>내가 고른 장난감. 게임 내내 강조된다</summary>
            MySecret,

            /// <summary>지금 고른 후보 (확인 대기 중)</summary>
            Candidate,

            /// <summary>지금은 클릭할 수 없음</summary>
            Dimmed,

            /// <summary>게임 종료</summary>
            Disabled
        }

        [Tooltip("이 타일의 슬롯 번호(0부터). 보드 전체에서 겹치지 않게 지정할 것.")]
        [SerializeField] private int slotIndex;

        [Header("표시 요소")]
        [SerializeField] private Image icon;
        [SerializeField] private GameObject secretMark;
        [SerializeField] private GameObject candidateMark;
        [SerializeField] private GameObject dimCover;

        public int SlotIndex { get { return slotIndex; } }

        /// <summary>클릭됐다. 인자는 슬롯 번호.</summary>
        public event Action<int> OnClicked;

        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(HandleClick);

            SetState(TileState.Normal);
        }

        private void HandleClick()
        {
            OnClicked?.Invoke(slotIndex);
        }

        /// <summary>이 타일에 놓일 장난감을 설정한다.</summary>
        public void SetToy(Sprite sprite, string displayName)
        {
            if (icon != null)
            {
                icon.sprite = sprite;

                // 장난감마다 비율이 달라서 이걸 켜두지 않으면 찌그러진다.
                icon.preserveAspect = true;
                icon.enabled = sprite != null;
            }

            // 디버깅할 때 하이어라키에서 바로 알아볼 수 있게 이름도 바꿔둔다.
            if (!string.IsNullOrEmpty(displayName))
            {
                gameObject.name = $"BoardTile_{slotIndex}_{displayName}";
            }
        }

        public void SetState(TileState state)
        {
            if (secretMark != null) secretMark.SetActive(state == TileState.MySecret);
            if (candidateMark != null) candidateMark.SetActive(state == TileState.Candidate);
            if (dimCover != null) dimCover.SetActive(state == TileState.Dimmed || state == TileState.Disabled);

            // MySecret은 강조 표시일 뿐이고, 그 위에 다시 클릭할 일도 있으므로 막지 않는다.
            if (_button != null)
            {
                _button.interactable = state != TileState.Dimmed && state != TileState.Disabled;
            }
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(HandleClick);
        }
    }
}
