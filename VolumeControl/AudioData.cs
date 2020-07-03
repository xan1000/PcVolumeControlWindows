using System.Collections.Generic;

namespace VolumeControl
{
    internal class PcAudio
    {
        public int protocolVersion;
        public string applicationVersion;
        public Dictionary<string,string> deviceIds = new Dictionary<string,string>();
        public AudioDevice defaultDevice;
    }

    internal class AudioDevice
    {
        public string deviceId;
        public string name;
        public float? masterVolume = null;
        public bool? masterMuted = null;
        public readonly List<AudioSession> sessions = new List<AudioSession>();

        public AudioDevice(string name, string deviceId)
        {
            this.name = name;
            this.deviceId = deviceId;
        }
    }

    internal class AudioSession
    {
        public string name;
        public string id;
        public float volume;
        public bool muted;

        public AudioSession(string name, string id, float volume, bool muted)
        {
            this.name = name;
            this.id = id;
            this.volume = volume;
            this.muted = muted;
        }
    }
}
