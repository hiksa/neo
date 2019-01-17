namespace Neo.Cryptography
{
    internal class MerkleTreeNode
    {
        public bool IsLeaf => this.LeftChild == null && this.RightChild == null;

        public bool IsRoot => this.Parent == null;

        public UInt256 Hash { get; set; }

        public MerkleTreeNode Parent { get; set; }

        public MerkleTreeNode LeftChild { get; set; }

        public MerkleTreeNode RightChild { get; set; }
    }
}
