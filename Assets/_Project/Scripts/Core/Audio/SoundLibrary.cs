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
            [Tooltip("Key of the sound.")]
            public string key;

            public AudioClip clip;

            [Tooltip("Default Volume of the sound")]
            [Range(0f, 1f)]
            public float volume = 1f;

            [Tooltip("It prevents the sound from being heard mechanically when the same sound effect is played consecutively. Keep the BGM at 0")]
            [Range(0f, 0.5f)]
            public float pitchVariance = 0f;
        }

        [Header("Background Music")]
        public List<SoundEntry> bgmList = new List<SoundEntry>();

        [Header("Sound Effect")]
        public List<SoundEntry> sfxList = new List<SoundEntry>();
    }
}
