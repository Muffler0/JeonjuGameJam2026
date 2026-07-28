using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Data
{
    /// <summary>
    /// 장난감 목록. itemId는 이 리스트의 인덱스다.
    ///
    /// 보드에는 이 목록에서 중복 없이 뽑은 것들이 올라간다.
    /// 타일 수(12)보다 많이 등록해두면 판마다 다른 조합이 나온다.
    /// </summary>
    [CreateAssetMenu(fileName = "ToyLibrary", menuName = "Project/Toy Library")]
    public class ToyLibrary : ScriptableObject
    {
        [Serializable]
        public class ToyEntry
        {
            [Tooltip("결과 화면 등에 표시할 이름")]
            public string displayName;

            public Sprite sprite;
        }

        [SerializeField] private List<ToyEntry> toys = new List<ToyEntry>();

        /// <summary>등록된 장난감 수. NetworkBridge의 Item Type Count와 맞출 것.</summary>
        public int Count { get { return toys.Count; } }

        public Sprite GetSprite(int itemId)
        {
            return IsValid(itemId) ? toys[itemId].sprite : null;
        }

        public string GetName(int itemId)
        {
            return IsValid(itemId) ? toys[itemId].displayName : "???";
        }

        private bool IsValid(int itemId)
        {
            if (itemId >= 0 && itemId < toys.Count) return true;

            Debug.LogWarning($"[ToyLibrary] 등록되지 않은 itemId: {itemId}");
            return false;
        }
    }
}
