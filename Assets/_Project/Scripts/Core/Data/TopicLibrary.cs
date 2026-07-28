using System.Collections.Generic;
using UnityEngine;

namespace Project.Data
{
    /// <summary>
    /// 주제 문구 목록. topicId는 이 리스트의 인덱스다.
    ///
    /// 한 판에 10턴 + 페널티분까지 중복 없이 뽑으므로 15개 이상 등록할 것.
    /// 부족하면 같은 주제가 다시 나오고 콘솔에 경고가 뜬다.
    /// </summary>
    [CreateAssetMenu(fileName = "TopicLibrary", menuName = "Project/Topic Library")]
    public class TopicLibrary : ScriptableObject
    {
        [Tooltip("설명할 때 제시되는 질문. 예: How big is this toy?")]
        [TextArea(1, 3)]
        [SerializeField] private List<string> topics = new List<string>();

        /// <summary>등록된 주제 수. NetworkBridge의 Topic Count와 맞출 것.</summary>
        public int Count { get { return topics.Count; } }

        public string GetTopic(int topicId)
        {
            if (topicId >= 0 && topicId < topics.Count) return topics[topicId];

            Debug.LogWarning($"[TopicLibrary] 등록되지 않은 topicId: {topicId}");
            return "???";
        }
    }
}
