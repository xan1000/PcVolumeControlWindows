using System.Collections.Generic;

namespace VolumeControl
{
    internal class PcAudio
    {
        public int protocolVersion;
        public AudioDevice defaultDevice;
    }

    internal class AudioDevice
    {
        public readonly string deviceId;
        public float? masterVolume = null;
        public bool? masterMuted = null;
        public readonly List<AudioSession> sessions = new List<AudioSession>();

        public AudioDevice(string deviceId)
        {
            this.deviceId = deviceId;
        }
    }

    internal class AudioSession
    {
        public readonly string id;
        public readonly float volume;
        public readonly bool muted;

        public AudioSession(string id, float volume, bool muted)
        {
            this.id = id;
            this.volume = volume;
            this.muted = muted;
        }
    }
}
