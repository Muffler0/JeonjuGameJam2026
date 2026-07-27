using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Core.Audio
{
    /// <summary>
    /// 사운드 클립 등록용 ScriptableObject.
    /// 프로그래머가 아니어도 인스펙터에서 클립을 드래그해 추가할 수 있다.
    /// 코드 수정 없이 사운드를 늘릴 수 있는 것이 이 방식의 목적.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundLibrary", menuName = "Project/Sound Library")]
    public class SoundLibrary : ScriptableObject
    {
        /// <summary>
        /// 사운드 한 개의 등록 정보.
        /// </summary>
        [Serializable]
        public class SoundEntry
        {
            [Tooltip("코드에서 호출할 때 쓰는 키. SoundKey 클래스의 상수와 문자열이 일치해야 한다.")]
            public string key;

            public AudioClip clip;

            [Tooltip("이 사운드 고유의 기본 볼륨. 클립마다 녹음 레벨이 달라서 여기서 맞춰준다.")]
            [Range(0f, 1f)]
            public float volume = 1f;

            [Tooltip("재생할 때마다 피치를 무작위로 흔드는 폭. 0.05면 ±5%. " +
                     "같은 효과음이 연속으로 날 때 기계적으로 들리는 것을 막아준다. BGM은 0으로 둘 것.")]
            [Range(0f, 0.5f)]
            public float pitchVariance = 0f;
        }

        [Header("배경음 (자동으로 루프 재생됨)")]
        public List<SoundEntry> bgmList = new List<SoundEntry>();

        [Header("효과음")]
        public List<SoundEntry> sfxList = new List<SoundEntry>();
    }
}
