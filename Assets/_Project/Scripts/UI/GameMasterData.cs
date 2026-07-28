using UnityEngine;

namespace Project.UI
{
    /// <summary>
    /// ルール層は itemId / topicId という数値しか扱わないため、
    /// 表示用の文字列はこちらで持つ。GameScreenUI と同じ GameObject に付ける。
    ///
    /// ★ topicTexts の個数は NetworkBridge の topicCount と必ず一致させること。
    ///   ずれると範囲外の topicId が届いて表示が崩れる。
    /// </summary>
    public class GameMasterData : MonoBehaviour
    {
        [Header("アイテム名（要素番号が itemId）")]
        [SerializeField]
        private string[] itemNames =
        {
            "りんご", "バナナ", "ぶどう", "みかん", "いちご", "メロン", "もも", "なし",
            "犬", "猫", "うさぎ", "きつね", "くま", "ぞう", "ぺんぎん", "いるか",
            "電車", "飛行機", "自転車", "バス", "船", "ロケット", "車", "バイク",
        };

        [Header("アイテム画像（要素番号が itemId。アイテム名と同じ並び順にすること）")]
        [SerializeField]
        private Sprite[] itemSprites;

        [Header("お題（要素番号が topicId）")]
        [SerializeField]
        private string[] topicTexts =
        {
            "色でたとえて", "音でたとえて", "大きさは？", "どこで見かける？",
            "手ざわりは？", "動物にたとえて", "季節は？", "値段は？",
            "重さは？", "においは？",
        };

        public int ItemCount => itemNames != null ? itemNames.Length : 0;
        public int TopicCount => topicTexts != null ? topicTexts.Length : 0;

        /// <summary>範囲外でも落ちないように、見つからなければ仮の文字列を返す。</summary>
        public string GetItemName(int itemId)
        {
            if (itemNames == null || itemId < 0 || itemId >= itemNames.Length)
            {
                return itemId < 0 ? "？" : $"item{itemId}";
            }

            return itemNames[itemId];
        }

        /// <summary>アイテム画像。未設定なら null（呼び出し側で画像を隠す）。</summary>
        public Sprite GetItemSprite(int itemId)
        {
            if (itemSprites == null || itemId < 0 || itemId >= itemSprites.Length) return null;
            return itemSprites[itemId];
        }

        public string GetTopicText(int topicId)
        {
            if (topicTexts == null || topicId < 0 || topicId >= topicTexts.Length)
            {
                return $"topic{topicId}";
            }

            return topicTexts[topicId];
        }
    }
}
