using System;
using System.Text;
namespace VisionInspect.Core.Models
{
    public sealed class CommunicationDataEventArgs : EventArgs
    {
        public CommunicationChannel Channel { get; private set; }
        public bool IsReceive { get; private set; }
        public byte[] Data { get; private set; }
        public DateTime Timestamp { get; private set; }
        public string Text { get { return Encoding.UTF8.GetString(Data).Replace("\r", "\\r").Replace("\n", "\\n"); } }
        public string Hex { get { return BitConverter.ToString(Data).Replace('-', ' '); } }
        public CommunicationDataEventArgs(CommunicationChannel channel, bool receive, byte[] data)
        { Channel = channel; IsReceive = receive; Data = (byte[])data.Clone(); Timestamp = DateTime.Now; }
    }
}
