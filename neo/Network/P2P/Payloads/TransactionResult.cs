namespace Neo.Network.P2P.Payloads
{
    public class TransactionResult
    {
        public UInt256 AssetId { get; set; }

        public Fixed8 Amount { get; set; }
    }
}
