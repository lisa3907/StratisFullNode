﻿using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.Blocks;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts.Managed;
using Stratis.Bitcoin.Features.Interop.Models;

namespace Stratis.Bitcoin.Features.Interop.EthereumClient
{
    public class EthereumClientBase : IEthereumClientBase
    {
        private readonly InteropSettings interopSettings;
        private readonly Web3 web3;
        private Event<TransferEventDTO> transferEventHandler;
        private NewFilterInput filterAllTransferEventsForContract;
        private HexBigInteger filterId;

        public const string ZeroAddress = "0x0000000000000000000000000000000000000000";

        public EthereumClientBase(InteropSettings interopSettings)
        {
            this.interopSettings = interopSettings;

            if (!this.interopSettings.Enabled)
                return;

            var account = new ManagedAccount(interopSettings.EthereumAccount, interopSettings.EthereumPassphrase);
            
            // TODO: Support loading offline accounts from keystore JSON directly?
            if (!string.IsNullOrWhiteSpace(interopSettings.EthereumClientUrl))
                this.web3 = new Web3(account, interopSettings.EthereumClientUrl);
            else
                this.web3 = new Web3(account);
        }

        public async Task CreateTransferEventFilterAsync()
        {
            this.transferEventHandler = this.web3.Eth.GetEvent<TransferEventDTO>(this.interopSettings.WrappedStraxAddress);
            this.filterAllTransferEventsForContract = this.transferEventHandler.CreateFilterInput();
            this.filterId = await this.transferEventHandler.CreateFilterAsync(this.filterAllTransferEventsForContract).ConfigureAwait(false);
        }

        public async Task<List<EventLog<TransferEventDTO>>> GetTransferEventsForWrappedStraxAsync()
        {
            try
            {
                // Note: this will only return events from after the filter is created.
                return await this.transferEventHandler.GetFilterChanges(this.filterId).ConfigureAwait(false);
            }
            catch (RpcResponseException)
            {
                // If the filter is no longer available it may need to be re-created.
                await this.CreateTransferEventFilterAsync().ConfigureAwait(false);
            }

            return await this.transferEventHandler.GetFilterChanges(this.filterId).ConfigureAwait(false);
        }

        public async Task<string> GetDestinationAddressAsync(string address)
        {
            return await WrappedStrax.GetDestinationAddressAsync(this.web3, this.interopSettings.WrappedStraxAddress, address).ConfigureAwait(false);
        }

        public async Task<BigInteger> GetBlockHeightAsync()
        {
            var blockNumberHandler = new EthBlockNumber(this.web3.Client);
            HexBigInteger block = await blockNumberHandler.SendRequestAsync().ConfigureAwait(false);
            
            return block.Value;
        }

        /// <inheritdoc />
        public async Task<BigInteger> SubmitTransactionAsync(string destination, BigInteger value, string data)
        {
            return await MultisigWallet.SubmitTransactionAsync(this.web3, this.interopSettings.MultisigWalletAddress, destination, value, data, this.interopSettings.EthereumGas, this.interopSettings.EthereumGasPrice).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> ConfirmTransactionAsync(BigInteger transactionId)
        {
            return await MultisigWallet.ConfirmTransactionAsync(this.web3, this.interopSettings.MultisigWalletAddress, transactionId, this.interopSettings.EthereumGas, this.interopSettings.EthereumGasPrice).ConfigureAwait(false);
        }

        public async Task<BigInteger> GetConfirmationCountAsync(BigInteger transactionId)
        {
            return await MultisigWallet.GetConfirmationCountAsync(this.web3, this.interopSettings.MultisigWalletAddress, transactionId).ConfigureAwait(false);
        }

        public string EncodeMintParams(string address, BigInteger amount)
        {
            // TODO: Extract this directly from the ABI
            const string MintMethod = "40c10f19";

            var abiEncode = new ABIEncode();
            string result = MintMethod + abiEncode.GetABIEncoded(new ABIValue("address", address), new ABIValue("uint256", amount)).ToHex();

            return result;
        }

        public string EncodeBurnParams(BigInteger amount, string straxAddress)
        {
            // TODO: Extract this directly from the ABI
            const string BurnMethod = "7641e6f3";

            var abiEncode = new ABIEncode();
            string result = BurnMethod + abiEncode.GetABIEncoded(new ABIValue("uint256", amount)).ToHex();

            return result;
        }

        public Dictionary<string, string> InvokeContract(InteropRequest request)
        {
            // TODO: Create an Ethereum transaction to invoke the specified method 
            return new Dictionary<string, string>();
        }

        public List<EthereumRequestModel> GetStratisInteropRequests()
        {
            // TODO: Filter the receipt logs for the interop contract and extract any interop requests
            return new List<EthereumRequestModel>();
        }

        public bool TransmitResponse(InteropRequest request)
        {
            // TODO: Send a transaction to the interop contract recording the results of the execution on the Cirrus network
            return true;
        }
    }
}
