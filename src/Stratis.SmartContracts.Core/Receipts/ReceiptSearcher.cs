﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin;
using Stratis.Bitcoin.Interfaces;

namespace Stratis.SmartContracts.Core.Receipts
{
    /// <summary>
    /// Searches the chain for receipts that match the given criteria and returns the receipts.
    /// </summary>
    public class ReceiptSearcher
    {
        private readonly ChainIndexer chainIndexer;
        private readonly IBlockStore blockStore;
        private readonly IReceiptRepository receiptRepository;
        private readonly Network network;
        private readonly ReceiptMatcher matcher;

        public ReceiptSearcher(ChainIndexer chainIndexer, IBlockStore blockStore, IReceiptRepository receiptRepository, Network network)
        {
            this.chainIndexer = chainIndexer;
            this.blockStore = blockStore;
            this.receiptRepository = receiptRepository;
            this.network = network;
            this.matcher = new ReceiptMatcher();
        }

        public List<Receipt> SearchReceipts(string eventName, int fromBlock, int? toBlock, IEnumerable<byte[]> topics)
        {
            var topicsList = new List<byte[]>();

            if (!string.IsNullOrWhiteSpace(eventName))
            {
                topicsList.Add(Encoding.UTF8.GetBytes(eventName));
            }

            if (topics != null)
            {
                topicsList.AddRange(topics);
            }

            return this.SearchReceipts((HashSet<string>)null, fromBlock, toBlock, topicsList);
        }

        public List<Receipt> SearchReceipts(string contractAddress, string eventName, int fromBlock, int? toBlock, IEnumerable<byte[]> topics)
        {
            var topicsList = new List<byte[]>();

            if (!string.IsNullOrWhiteSpace(eventName))
            {
                topicsList.Add(Encoding.UTF8.GetBytes(eventName));
            }

            if (topics != null)
            {
                topicsList.AddRange(topics);
            }

            return this.SearchReceipts(new HashSet<string>() { contractAddress }, fromBlock, toBlock, topicsList);
        }

        public List<Receipt> SearchReceipts(HashSet<string> contractAddresses, string eventName, int fromBlock, int? toBlock, IEnumerable<byte[]> topics)
        {
            var topicsList = new List<byte[]>();

            if (!string.IsNullOrWhiteSpace(eventName))
            {
                topicsList.Add(Encoding.UTF8.GetBytes(eventName));
            }

            if (topics != null)
            {
                topicsList.AddRange(topics);
            }

            return this.SearchReceipts(contractAddresses, fromBlock, toBlock, topicsList);
        }

        public List<Receipt> SearchReceipts(HashSet<string> contractAddresses, int fromBlock = 0, int? toBlock = null, IEnumerable<byte[]> topics = null)
        {
            topics = topics?.Where(topic => topic != null) ?? Enumerable.Empty<byte[]>();

            // Ensure that we perform the Keccak256 hash calculations only once before entering the loop.
            // This leads to "only" a two-fold speed improvement mostly because the db header retrieval is still slow.
            var filterBloom = new Bloom();

            var addressesUint160 = new HashSet<uint160>();

            if (contractAddresses != null)
            {
                foreach (string contractAddress in contractAddresses)
                {
                    // Build the bytes we can use to check for this event.
                    // TODO use address.ToUint160 extension when it is in .Core.
                    var addressUint160 = new uint160(new BitcoinPubKeyAddress(contractAddress, this.network).Hash.ToBytes());

                    addressesUint160.Add(addressUint160);
                    filterBloom.Add(addressUint160.ToBytes());
                }
            }

            foreach (byte[] topic in topics)
            {
                if (topic != null)
                {
                    filterBloom.Add(topic);
                }
            }

            IEnumerable<ChainedHeader> blockHeaders = this.chainIndexer[toBlock ?? this.chainIndexer.Tip.Height]
                .EnumerateToGenesis()
                .TakeWhile(c => c.Height >= fromBlock)
                .Reverse();

            // Match the blocks where the combination of all receipts passes the filter.
            var matches = new List<ChainedHeader>();
            foreach (ChainedHeader chainedHeader in blockHeaders)
            {
                var scHeader = (ISmartContractBlockHeader)chainedHeader.Header;

                if (scHeader.LogsBloom.Test(filterBloom))
                    matches.Add(chainedHeader);
            }

            // For all matching headers, get the block from local db.
            List<uint256> matchedBlockHashes = matches.Select(m => m.HashBlock).ToList();
            List<NBitcoin.Block> blocks = this.blockStore.GetBlocks(matchedBlockHashes);

            List<uint256> transactionHashes = blocks
                .SelectMany(block => block.Transactions)
                .Select(t => t.GetHash())
                .ToList();

            IList<Receipt> receipts = this.receiptRepository.RetrieveMany(transactionHashes);

            // For each block, get all receipts, and if they match, add to list to return.
            return this.matcher.MatchReceipts(receipts, addressesUint160, topics);
        }
    }
}
