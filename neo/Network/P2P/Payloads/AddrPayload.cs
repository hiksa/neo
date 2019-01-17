using System;
using System.IO;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Network.P2P.Payloads
{
    public class AddrPayload : ISerializable
    {
        public const int MaxCountToSend = 200;

        public NetworkAddressWithTime[] AddressList { get; private set; }

        public int Size => this.AddressList.GetVarSize();

        public static AddrPayload Create(params NetworkAddressWithTime[] addresses)
        {
            return new AddrPayload
            {
                AddressList = addresses
            };
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.AddressList = reader.ReadSerializableArray<NetworkAddressWithTime>(AddrPayload.MaxCountToSend);
            if (this.AddressList.Length == 0)
            {
                throw new FormatException();
            }
        }

        void ISerializable.Serialize(BinaryWriter writer) =>
            writer.Write(this.AddressList);        
    }
}
