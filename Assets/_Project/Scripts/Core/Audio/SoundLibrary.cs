using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Core.Audio
{
    /// <summary>
    /// 사운드 클립 등록용 ScriptableObject.
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
            [Tooltip("Key used when calling from code. The constant and string of the SoundKey class must match.")]
            public string key;

            public AudioClip clip;

            [Tooltip("Default volume of this sound.")]
            [Range(0f, 1f)]
            public float volume = 1f;

            [Tooltip("set the BGM to 0")]
            [Range(0f, 0.5f)]
            public float pitchVariance = 0f;
        }

        [Header("BGM")]
        public List<SoundEntry> bgmList = new List<SoundEntry>();

        [Header("SFX")]
        public List<SoundEntry> sfxList = new List<SoundEntry>();
    }
}
