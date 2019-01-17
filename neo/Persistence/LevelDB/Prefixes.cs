namespace Neo.Persistence.LevelDB
{
    internal static class Prefixes
    {
        public const byte DataBlock = 0x01;
        public const byte DataTransaction = 0x02;

        public const byte STAccount = 0x40;
        public const byte STCoin = 0x44;
        public const byte STSpentCoin = 0x45;
        public const byte STValidator = 0x48;
        public const byte STAsset = 0x4c;
        public const byte STContract = 0x50;
        public const byte STStorage = 0x70;

        public const byte IXHeaderHashList = 0x80;
        public const byte IXValidatorsCount = 0x90;
        public const byte IXCurrentBlock = 0xc0;
        public const byte IXCurrentHeader = 0xc1;

        public const byte SYSVersion = 0xf0;
    }
}
