using System;
using System.IO;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Network.P2P.Payloads
{
    public class VersionPayload : ISerializable
    {
        public uint Version { get; private set; }

        public ulong Services { get; private set; }

        public uint Timestamp { get; private set; }

        public ushort Port { get; private set; }

        public uint Nonce { get; private set; }

        public string UserAgent { get; private set; }

        public uint StartHeight { get; private set; }

        public bool Relay { get; private set; }

        public int Size => 
            sizeof(uint) + sizeof(ulong) + sizeof(uint) 
            + sizeof(ushort) + sizeof(uint) + this.UserAgent.GetVarSize() 
            + sizeof(uint) + sizeof(bool);

        public static VersionPayload Create(int port, uint nonce, string userAgent, uint startHeight)
        {
            return new VersionPayload
            {
                Version = LocalNode.ProtocolVersion,
                Services = NetworkAddressWithTime.NodeNetwork,
                Timestamp = DateTime.Now.ToTimestamp(),
                Port = (ushort)port,
                Nonce = nonce,
                UserAgent = userAgent,
                StartHeight = startHeight,
                Relay = true
            };
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.Version = reader.ReadUInt32();
            this.Services = reader.ReadUInt64();
            this.Timestamp = reader.ReadUInt32();
            this.Port = reader.ReadUInt16();
            this.Nonce = reader.ReadUInt32();
            this.UserAgent = reader.ReadVarString(1024);
            this.StartHeight = reader.ReadUInt32();
            this.Relay = reader.ReadBoolean();
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(this.Version);
            writer.Write(this.Services);
            writer.Write(this.Timestamp);
            writer.Write(this.Port);
            writer.Write(this.Nonce);
            writer.WriteVarString(this.UserAgent);
            writer.Write(this.StartHeight);
            writer.Write(this.Relay);
        }
    }
}
