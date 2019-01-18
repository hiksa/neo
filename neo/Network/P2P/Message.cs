using System;
using System.IO;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Network.P2P
{
    public class Message : ISerializable
    {
        public const int HeaderSize = sizeof(uint) + 12 + sizeof(int) + sizeof(uint);

        public const int PayloadMaxSize = 0x02000000;

        public static readonly uint Magic = ProtocolSettings.Default.Magic;

        public string Command { get; private set; }

        public uint Checksum { get; private set; }

        public byte[] Payload { get; private set; }

        public int Size => HeaderSize + this.Payload.Length;

        public static Message Create(string command, ISerializable payload = null) =>
            Message.Create(command, payload == null ? new byte[0] : payload.ToArray());

        public static Message Create(string command, byte[] payload) =>
            new Message
            {
                Command = command,
                Checksum = Message.GetChecksum(payload),
                Payload = payload
            };

        void ISerializable.Deserialize(BinaryReader reader)
        {
            if (reader.ReadUInt32() != Message.Magic)
            {
                throw new FormatException();
            }

            this.Command = reader.ReadFixedString(12);

            var length = reader.ReadUInt32();
            if (length > Message.PayloadMaxSize)
            {
                throw new FormatException();
            }

            this.Checksum = reader.ReadUInt32();
            this.Payload = reader.ReadBytes((int)length);

            if (Message.GetChecksum(this.Payload) != this.Checksum)
            {
                throw new FormatException();
            }
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(Message.Magic);
            writer.WriteFixedString(this.Command, 12);
            writer.Write(this.Payload.Length);
            writer.Write(this.Checksum);
            writer.Write(this.Payload);
        }

        private static uint GetChecksum(byte[] value) => Crypto.Default.Hash256(value).ToUInt32(0);
    }
}
