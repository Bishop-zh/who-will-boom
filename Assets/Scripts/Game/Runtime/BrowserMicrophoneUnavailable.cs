#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine;
namespace BombTavern.Runtime
{
    internal static class BrowserMicrophoneUnavailable
    {
        public static string[] devices => System.Array.Empty<string>();
        public static AudioClip Start(string device, bool loop, int seconds, int frequency) => null;
        public static bool IsRecording(string device) => false;
        public static void End(string device) { }
        public static int GetPosition(string device) => 0;
    }
}
#endif
