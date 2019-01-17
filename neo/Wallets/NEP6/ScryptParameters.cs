using Neo.IO.Json;

namespace Neo.Wallets.NEP6
{
    public class ScryptParameters
    {
        public readonly int N;
        public readonly int R;
        public readonly int P;

        public ScryptParameters(int n, int r, int p)
        {
            this.N = n;
            this.R = r;
            this.P = p;
        }

        public static ScryptParameters Default { get; } = new ScryptParameters(16384, 8, 8);

        public static ScryptParameters FromJson(JObject json) =>
            new ScryptParameters(
                (int)json["n"].AsNumber(), 
                (int)json["r"].AsNumber(), 
                (int)json["p"].AsNumber());

        public JObject ToJson()
        {
            var json = new JObject();
            json["n"] = this.N;
            json["r"] = this.R;
            json["p"] = this.P;
            return json;
        }
    }
}
