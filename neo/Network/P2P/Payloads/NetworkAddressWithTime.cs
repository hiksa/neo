using System;
using System.IO;
using System.Linq;
using System.Net;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Network.P2P.Payloads
{
    public class NetworkAddressWithTime : ISerializable
    {
        public const ulong NodeNetwork = 1;

        public uint Timestamp { get; private set; }

        public ulong Services { get; private set; }

        public IPEndPoint EndPoint { get; private set; }

        public int Size => sizeof(uint) + sizeof(ulong) + 16 + sizeof(ushort);

        public static NetworkAddressWithTime Create(IPEndPoint endpoint, ulong services, uint timestamp)
        {
            var result = new NetworkAddressWithTime
            {
                Timestamp = timestamp,
                Services = services,
                EndPoint = endpoint
            };

            return result;
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.Timestamp = reader.ReadUInt32();
            this.Services = reader.ReadUInt64();

            var data = reader.ReadBytes(16);
            if (data.Length != 16)
            {
                throw new FormatException();
            }

            var address = new IPAddress(data).Unmap();
            data = reader.ReadBytes(2);
            if (data.Length != 2)
            {
                throw new FormatException();
            }

            ushort port = data
                .Reverse()
                .ToArray()
                .ToUInt16(0);

            this.EndPoint = new IPEndPoint(address, port);
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(this.Timestamp);
            writer.Write(this.Services);

            var addressBytes = this.EndPoint.Address.MapToIPv6().GetAddressBytes();
            writer.Write(addressBytes);

            var portBytes = BitConverter.GetBytes((ushort)this.EndPoint.Port).Reverse().ToArray();
            writer.Write(portBytes);
        }
    }
}
