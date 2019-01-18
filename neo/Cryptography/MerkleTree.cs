using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neo.Cryptography
{
    public class MerkleTree
    {
        private MerkleTreeNode root;

        internal MerkleTree(UInt256[] hashes)
        {
            if (hashes == null || hashes.Length == 0)
            {
                throw new ArgumentException("Merkle Tree initialization requires at least one hash.");
            }

            var leafNodes = hashes.Select(p => new MerkleTreeNode { Hash = p }).ToArray();
            this.root = MerkleTree.Build(leafNodes);

            int depth = 1;
            for (var node = this.root; node.LeftChild != null; node = node.LeftChild)
            {
                depth++;
            }

            this.Depth = depth;
        }

        public int Depth { get; private set; }

        public static UInt256 ComputeRoot(UInt256[] hashes)
        {
            if (hashes.Length == 0)
            {
                throw new ArgumentException();
            }

            if (hashes.Length == 1)
            {
                return hashes[0];
            }

            var tree = new MerkleTree(hashes);
            return tree.root.Hash;
        }

        // depth-first order
        public UInt256[] ToHashArray()
        {
            var hashes = new List<UInt256>();
            MerkleTree.DepthFirstSearch(this.root, hashes);
            return hashes.ToArray();
        }

        public void Trim(BitArray flags)
        {
            flags = new BitArray(flags);
            flags.Length = 1 << (this.Depth - 1);

            MerkleTree.Trim(this.root, 0, this.Depth, flags);
        }

        private static void DepthFirstSearch(MerkleTreeNode node, IList<UInt256> hashes)
        {
            if (node.LeftChild == null)
            {
                // if left is null, then right must be null
                hashes.Add(node.Hash);
            }
            else
            {
                MerkleTree.DepthFirstSearch(node.LeftChild, hashes);
                MerkleTree.DepthFirstSearch(node.RightChild, hashes);
            }
        }

        private static void Trim(MerkleTreeNode node, int index, int depth, BitArray flags)
        {
            if (depth == 1)
            {
                return;
            }

            if (node.LeftChild == null)
            {
                return; // if left is null, then right must be null
            }

            if (depth == 2)
            {
                if (!flags.Get(index * 2) && !flags.Get((index * 2) + 1))
                {
                    node.LeftChild = null;
                    node.RightChild = null;
                }
            }
            else
            {
                MerkleTree.Trim(node.LeftChild, index * 2, depth - 1, flags);
                MerkleTree.Trim(node.RightChild, (index * 2) + 1, depth - 1, flags);

                if (node.LeftChild.LeftChild == null && node.RightChild.RightChild == null)
                {
                    node.LeftChild = null;
                    node.RightChild = null;
                }
            }
        }

        private static MerkleTreeNode Build(MerkleTreeNode[] leaves)
        {
            if (leaves.Length == 0)
            {
                throw new ArgumentException("Building a Merkle Tree requires at least one leaf node.");
            }

            if (leaves.Length == 1)
            {
                return leaves[0];
            }

            var parentNodes = new MerkleTreeNode[(leaves.Length + 1) / 2];
            for (int i = 0; i < parentNodes.Length; i++)
            {
                parentNodes[i] = new MerkleTreeNode { LeftChild = leaves[i * 2] };
                leaves[i * 2].Parent = parentNodes[i];

                if ((i * 2) + 1 == leaves.Length)
                {
                    parentNodes[i].RightChild = parentNodes[i].LeftChild;
                }
                else
                {
                    parentNodes[i].RightChild = leaves[(i * 2) + 1];
                    leaves[(i * 2) + 1].Parent = parentNodes[i];
                }

                var leftChildHash = parentNodes[i].LeftChild.Hash;
                var rightChildHash = parentNodes[i].RightChild.Hash;
                var combinedHashes = leftChildHash
                    .ToArray()
                    .Concat(rightChildHash.ToArray())
                    .ToArray();

                var parentHashBytes = Crypto.Default.Hash256(combinedHashes);
                parentNodes[i].Hash = new UInt256(parentHashBytes);
            }

            return MerkleTree.Build(parentNodes); // TailCall
        }
    }
}
