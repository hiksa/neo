using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Ledger.States;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Plugins;
using Neo.SmartContract;
using Neo.VM;
using Neo.Wallets;
using Neo.Wallets.NEP6;

namespace Neo.Network.RPC
{
    public sealed class RpcServer : IDisposable
    {
        public Wallet Wallet;

        private readonly NeoSystem system;
        private IWebHost host;
        private Fixed8 maxGasInvoke;

        public RpcServer(NeoSystem system, Wallet wallet = null, Fixed8 maxGasInvoke = default(Fixed8))
        {
            this.system = system;
            this.Wallet = wallet;
            this.maxGasInvoke = maxGasInvoke;
        }

        public void Dispose()
        {
            if (this.host != null)
            {
                this.host.Dispose();
                this.host = null;
            }
        }

        public void OpenWallet(Wallet wallet)
        {
            this.Wallet = wallet;
        }

        public void Start(
            IPAddress bindAddress,
            int port,
            string sslCert = null,
            string password = null,
            string[] trustedAuthorities = null)
        {
            this.host = new WebHostBuilder()
                .UseKestrel(options => options.Listen(bindAddress, port, listenOptions =>
                {
                    if (string.IsNullOrEmpty(sslCert))
                    {
                        return;
                    }

                    listenOptions.UseHttps(sslCert, password, httpsConnectionAdapterOptions =>
                    {
                        if (trustedAuthorities is null || trustedAuthorities.Length == 0)
                        {
                            return;
                        }

                        httpsConnectionAdapterOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                        httpsConnectionAdapterOptions.ClientCertificateValidation = (cert, chain, err) =>
                        {
                            if (err != SslPolicyErrors.None)
                            {
                                return false;
                            }

                            var authority = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                            return trustedAuthorities.Contains(authority.Thumbprint);
                        };
                    });
                }))
                .Configure(app =>
                {
                    app.UseResponseCompression();
                    app.Run(ProcessAsync);
                })
                .ConfigureServices(services =>
                {
                    services.AddResponseCompression(options =>
                    {
                        // options.EnableForHttps = false;
                        options.Providers.Add<GzipCompressionProvider>();
                        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/json-rpc" });
                    });

                    services.Configure<GzipCompressionProviderOptions>(options =>
                    {
                        options.Level = CompressionLevel.Fastest;
                    });
                })
                .Build();

            this.host.Start();
        }

        private static JObject GetRelayResult(RelayResultReason reason)
        {
            switch (reason)
            {
                case RelayResultReason.Succeed:
                    return true;
                case RelayResultReason.AlreadyExists:
                    throw new RpcException(-501, "Block or transaction already exists and cannot be sent repeatedly.");
                case RelayResultReason.OutOfMemory:
                    throw new RpcException(-502, "The memory pool is full and no more transactions can be sent.");
                case RelayResultReason.UnableToVerify:
                    throw new RpcException(-503, "The block cannot be validated.");
                case RelayResultReason.Invalid:
                    throw new RpcException(-504, "Block or transaction validation failed.");
                case RelayResultReason.PolicyFail:
                    throw new RpcException(-505, "One of the Policy filters failed.");
                default:
                    throw new RpcException(-500, "Unknown error.");
            }
        }

        private static JObject CreateErrorResponse(JObject id, int code, string message, JObject data = null)
        {
            var response = CreateResponse(id);
            response["error"] = new JObject();
            response["error"]["code"] = code;
            response["error"]["message"] = message;
            if (data != null)
            {
                response["error"]["data"] = data;
            }

            return response;
        }

        private static JObject CreateResponse(JObject id)
        {
            var response = new JObject();
            response["jsonrpc"] = "2.0";
            response["id"] = id;
            return response;
        }

        private JObject GetInvokeResult(byte[] script)
        {
            var engine = ApplicationEngine.Run(script, extraGAS: this.maxGasInvoke);
            var json = new JObject();
            json["script"] = script.ToHexString();
            json["state"] = engine.State;
            json["gas_consumed"] = engine.GasConsumed.ToString();
            try
            {
                json["stack"] = new JArray(engine.ResultStack.Select(p => p.ToParameter().ToJson()));
            }
            catch (InvalidOperationException)
            {
                json["stack"] = "error: recursive reference";
            }

            if (this.Wallet != null)
            {
                var tx = new InvocationTransaction
                {
                    Version = 1,
                    Script = json["script"].AsString().HexToBytes(),
                    Gas = Fixed8.Parse(json["gas_consumed"].AsString())
                };

                tx.Gas -= Fixed8.FromDecimal(10);
                if (tx.Gas < Fixed8.Zero)
                {
                    tx.Gas = Fixed8.Zero;
                }

                tx.Gas = tx.Gas.Ceiling();
                tx = this.Wallet.MakeTransaction(tx);
                if (tx != null)
                {
                    var context = new ContractParametersContext(tx);
                    this.Wallet.Sign(context);
                    if (context.Completed)
                    {
                        tx.Witnesses = context.GetWitnesses();
                    }
                    else
                    {
                        tx = null;
                    }
                }

                json["tx"] = tx?.ToArray().ToHexString();
            }

            return json;
        }

        private JObject Process(string method, JArray requestParams)
        {
            switch (method)
            {
                case "dumpprivkey":
                    if (this.Wallet == null)
                    {
                        throw new RpcException(-400, "Access denied");
                    }
                    else
                    {
                        var scriptHash = requestParams[0].AsString().ToScriptHash();
                        var account = this.Wallet.GetAccount(scriptHash);
                        return account.GetKey().Export();
                    }

                case "getaccountstate":
                    {
                        var scriptHash = requestParams[0].AsString().ToScriptHash();
                        var account = Blockchain.Instance
                            .Store
                            .GetAccounts()
                            .TryGet(scriptHash) ?? new AccountState(scriptHash);

                        return account.ToJson();
                    }

                case "getassetstate":
                    {
                        var assetId = UInt256.Parse(requestParams[0].AsString());
                        var asset = Blockchain.Instance.Store.GetAssets().TryGet(assetId);
                        return asset?.ToJson() ?? throw new RpcException(-100, "Unknown asset");
                    }

                case "getbalance":
                    if (this.Wallet == null)
                    {
                        throw new RpcException(-400, "Access denied.");
                    }
                    else
                    {
                        var json = new JObject();
                        var assetId = UIntBase.Parse(requestParams[0].AsString());
                        switch (assetId)
                        {
                            case UInt160 assetId160: // NEP-5 balance
                                json["balance"] = this.Wallet.GetAvailable(assetId160).ToString();
                                break;
                            case UInt256 assetId256: // Global Assets balance
                                var coins = this.Wallet
                                    .GetCoins()
                                    .Where(p => !p.State.HasFlag(CoinStates.Spent))
                                    .Where(p => p.Output.AssetId.Equals(assetId256));
                                var balance = coins.Sum(p => p.Output.Value).ToString();
                                var confirmed = coins
                                    .Where(p => p.State.HasFlag(CoinStates.Confirmed))
                                    .Sum(p => p.Output.Value)
                                    .ToString();

                                json["balance"] = balance;
                                json["confirmed"] = confirmed;
                                break;
                        }

                        return json;
                    }

                case "getbestblockhash":
                    return Blockchain.Instance.CurrentBlockHash.ToString();

                case "getblock":
                    {
                        Block block;
                        if (requestParams[0] is JNumber)
                        {
                            var index = (uint)requestParams[0].AsNumber();
                            block = Blockchain.Instance.Store.GetBlock(index);
                        }
                        else
                        {
                            var hash = UInt256.Parse(requestParams[0].AsString());
                            block = Blockchain.Instance.Store.GetBlock(hash);
                        }

                        if (block == null)
                        {
                            throw new RpcException(-100, "Unknown block");
                        }

                        var verbose = requestParams.Count >= 2 && requestParams[1].AsBooleanOrDefault(false);
                        if (verbose)
                        {
                            var json = block.ToJson();
                            json["confirmations"] = Blockchain.Instance.Height - block.Index + 1;

                            var hash = Blockchain.Instance.Store.GetNextBlockHash(block.Hash);
                            if (hash != null)
                            {
                                json["nextblockhash"] = hash.ToString();
                            }

                            return json;
                        }

                        return block.ToArray().ToHexString();
                    }

                case "getblockcount":
                    return Blockchain.Instance.Height + 1;

                case "getblockhash":
                    {
                        var height = (uint)requestParams[0].AsNumber();
                        if (height <= Blockchain.Instance.Height)
                        {
                            return Blockchain.Instance.GetBlockHash(height).ToString();
                        }

                        throw new RpcException(-100, "Invalid Height");
                    }

                case "getblockheader":
                    {
                        Header header;
                        if (requestParams[0] is JNumber)
                        {
                            var height = (uint)requestParams[0].AsNumber();
                            header = Blockchain.Instance.Store.GetHeader(height);
                        }
                        else
                        {
                            var hash = UInt256.Parse(requestParams[0].AsString());
                            header = Blockchain.Instance.Store.GetHeader(hash);
                        }

                        if (header == null)
                        {
                            throw new RpcException(-100, "Unknown block");
                        }

                        var verbose = requestParams.Count >= 2 && requestParams[1].AsBooleanOrDefault(false);
                        if (verbose)
                        {
                            var json = header.ToJson();
                            json["confirmations"] = Blockchain.Instance.Height - header.Index + 1;

                            var hash = Blockchain.Instance.Store.GetNextBlockHash(header.Hash);
                            if (hash != null)
                            {
                                json["nextblockhash"] = hash.ToString();
                            }

                            return json;
                        }

                        return header.ToArray().ToHexString();
                    }

                case "getblocksysfee":
                    {
                        var blockHeight = (uint)requestParams[0].AsNumber();
                        if (blockHeight <= Blockchain.Instance.Height)
                        {
                            return Blockchain.Instance.Store.GetSysFeeAmount(blockHeight).ToString();
                        }

                        throw new RpcException(-100, "Invalid Height");
                    }

                case "getconnectioncount":
                    return LocalNode.Instance.ConnectedCount;

                case "getcontractstate":
                    {
                        var scriptHash = UInt160.Parse(requestParams[0].AsString());
                        var contract = Blockchain.Instance.Store.GetContracts().TryGet(scriptHash);

                        return contract?.ToJson() ?? throw new RpcException(-100, "Unknown contract");
                    }

                case "getnewaddress":
                    if (this.Wallet == null)
                    {
                        throw new RpcException(-400, "Access denied");
                    }
                    else
                    {
                        var account = Wallet.CreateAccount();
                        if (this.Wallet is NEP6Wallet nep6)
                        {
                            nep6.Save();
                        }

                        return account.Address;
                    }

                case "getpeers":
                    {
                        var unconnectedPeers = LocalNode.Instance
                            .GetUnconnectedPeers()
                            .Select(p =>
                            {
                                var peerJson = new JObject();
                                peerJson["address"] = p.Address.ToString();
                                peerJson["port"] = p.Port;
                                return peerJson;
                            });

                        var connectedPeers = LocalNode.Instance
                            .GetRemoteNodes()
                            .Select(p =>
                            {
                                var peerJson = new JObject();
                                peerJson["address"] = p.Remote.Address.ToString();
                                peerJson["port"] = p.ListenerPort;
                                return peerJson;
                            });

                        var json = new JObject();
                        json["unconnected"] = new JArray(unconnectedPeers);
                        json["bad"] = new JArray(); // bad peers have been removed
                        json["connected"] = new JArray(connectedPeers);

                        return json;
                    }

                case "getrawmempool":
                    {
                        var items = Blockchain.Instance.GetMemoryPool().Select(p => (JObject)p.Hash.ToString());
                        return new JArray(items);
                    }

                case "getrawtransaction":
                    {
                        var hash = UInt256.Parse(requestParams[0].AsString());
                        var verbose = requestParams.Count >= 2 && requestParams[1].AsBooleanOrDefault(false);
                        var tx = Blockchain.Instance.GetTransaction(hash);
                        if (tx == null)
                        {
                            throw new RpcException(-100, "Unknown transaction");
                        }

                        if (verbose)
                        {
                            var json = tx.ToJson();
                            var height = Blockchain.Instance.Store.GetTransactions().TryGet(hash)?.BlockIndex;
                            if (height != null)
                            {
                                var header = Blockchain.Instance.Store.GetHeader((uint)height);
                                json["blockhash"] = header.Hash.ToString();
                                json["confirmations"] = Blockchain.Instance.Height - header.Index + 1;
                                json["blocktime"] = header.Timestamp;
                            }

                            return json;
                        }

                        return tx.ToArray().ToHexString();
                    }

                case "getstorage":
                    {
                        var scriptHash = UInt160.Parse(requestParams[0].AsString());
                        var key = requestParams[1].AsString().HexToBytes();
                        var storageKey = new StorageKey
                        {
                            ScriptHash = scriptHash,
                            Key = key
                        };

                        var item = Blockchain.Instance
                            .Store
                            .GetStorages()
                            .TryGet(storageKey) ?? new StorageItem();

                        return item.Value?.ToHexString();
                    }

                case "gettransactionheight":
                    {
                        var hash = UInt256.Parse(requestParams[0].AsString());
                        var height = Blockchain.Instance.Store.GetTransactions().TryGet(hash)?.BlockIndex;
                        if (height.HasValue)
                        {
                            return height.Value;
                        }

                        throw new RpcException(-100, "Unknown transaction");
                    }

                case "gettxout":
                    {
                        var hash = UInt256.Parse(requestParams[0].AsString());
                        var index = (ushort)requestParams[1].AsNumber();
                        return Blockchain.Instance.Store.GetUnspent(hash, index)?.ToJson(index);
                    }

                case "getvalidators":
                    using (var snapshot = Blockchain.Instance.GetSnapshot())
                    {
                        var validators = snapshot.GetValidators();
                        var result = snapshot
                            .GetEnrollments()
                            .Select(p =>
                            {
                                var validator = new JObject();
                                validator["publickey"] = p.PublicKey.ToString();
                                validator["votes"] = p.Votes.ToString();
                                validator["active"] = validators.Contains(p.PublicKey);
                                return validator;
                            })
                            .ToArray();

                        return result;
                    }

                case "getversion":
                    {
                        var json = new JObject();
                        json["port"] = LocalNode.Instance.ListenerPort;
                        json["nonce"] = LocalNode.Nonce;
                        json["useragent"] = LocalNode.UserAgent;
                        return json;
                    }

                case "getwalletheight":
                    if (this.Wallet == null)
                    {
                        throw new RpcException(-400, "Access denied.");
                    }
                    else
                    {
                        return (this.Wallet.WalletHeight > 0) ? this.Wallet.WalletHeight - 1 : 0;
                    }

                case "invoke":
                    {
                        var scriptHash = UInt160.Parse(requestParams[0].AsString());
                        var parameters = ((JArray)requestParams[1])
                            .Select(p => ContractParameter.FromJson(p))
                            .ToArray();

                        byte[] script;
                        using (var sb = new ScriptBuilder())
                        {
                            script = sb.EmitAppCall(scriptHash, parameters).ToArray();
                        }

                        return this.GetInvokeResult(script);
                    }

                case "invokefunction":
                    {
                        var scriptHash = UInt160.Parse(requestParams[0].AsString());
                        var operation = requestParams[1].AsString();
                        var parameters = requestParams.Count >= 3 
                            ? ((JArray)requestParams[2]).Select(p => ContractParameter.FromJson(p)).ToArray() 
                            : new ContractParameter[0];

                        byte[] script;
                        using (var sb = new ScriptBuilder())
                        {
                            script = sb.EmitAppCall(scriptHash, operation, parameters).ToArray();
                        }

                        return this.GetInvokeResult(script);
                    }

                case "invokescript":
                    {
                        var script = requestParams[0].AsString().HexToBytes();
                        return this.GetInvokeResult(script);
                    }

                case "listaddress":
                    if (this.Wallet == null)
                    {
                        throw new RpcException(-400, "Access denied.");
                    }
                    else
                    {
                        var walletAccountsJson = this.Wallet
                            .GetAccounts()
                            .Select(p =>
                            {
                                var account = new JObject();
                                account["address"] = p.Address;
                                account["haskey"] = p.HasKey;
                                account["label"] = p.Label;
                                account["watchonly"] = p.WatchOnly;
                                return account;
                            })
                            .ToArray();

                        return walletAccountsJson;
                    }

                case "sendfrom":
                    if (this.Wallet == null)
                    {
                        throw new RpcException(-400, "Access denied");
                    }
                    else
                    {
                        var assetId = UIntBase.Parse(requestParams[0].AsString());
                        var descriptor = new AssetDescriptor(assetId);
                        var from = requestParams[1].AsString().ToScriptHash();
                        var to = requestParams[2].AsString().ToScriptHash();
                        var value = BigDecimal.Parse(requestParams[3].AsString(), descriptor.Decimals);
                        if (value.Sign <= 0)
                        {
                            throw new RpcException(-32602, "Invalid params");
                        }

                        var fee = requestParams.Count >= 5 
                            ? Fixed8.Parse(requestParams[4].AsString()) 
                            : Fixed8.Zero;

                        if (fee < Fixed8.Zero)
                        {
                            throw new RpcException(-32602, "Invalid params");
                        }

                        var changeAddress = requestParams.Count >= 6 
                            ? requestParams[5].AsString().ToScriptHash() 
                            : null;

                        var outputs = new[] 
                        {
                            new TransferOutput { AssetId = assetId, Value = value, ScriptHash = to }
                        };

                        var tx = this.Wallet.MakeTransaction(null, outputs, from, changeAddress, fee);
                        if (tx == null)
                        {
                            throw new RpcException(-300, "Insufficient funds");
                        }

                        var parametersContext = new ContractParametersContext(tx);
                        this.Wallet.Sign(parametersContext);

                        if (parametersContext.Completed)
                        {
                            tx.Witnesses = parametersContext.GetWitnesses();

                            this.Wallet.ApplyTransaction(tx);

                            var relayMessage = new LocalNode.Relay(tx);
                            this.system.LocalNodeActorRef.Tell(relayMessage);

                            return tx.ToJson();
                        }
                        else
                        {
                            return parametersContext.ToJson();
                        }
                    }

                case "sendmany":
                    if (this.Wallet == null)
                    {
                        throw new RpcException(-400, "Access denied");
                    }
                    else
                    {
                        var to = (JArray)requestParams[0];
                        if (to.Count == 0)
                        {
                            throw new RpcException(-32602, "Invalid params");
                        }

                        var outputs = new TransferOutput[to.Count];
                        for (int i = 0; i < to.Count; i++)
                        {
                            var assetId = UIntBase.Parse(to[i]["asset"].AsString());
                            var descriptor = new AssetDescriptor(assetId);
                            outputs[i] = new TransferOutput
                            {
                                AssetId = assetId,
                                Value = BigDecimal.Parse(to[i]["value"].AsString(), descriptor.Decimals),
                                ScriptHash = to[i]["address"].AsString().ToScriptHash()
                            };

                            if (outputs[i].Value.Sign <= 0)
                            {
                                throw new RpcException(-32602, "Invalid params");
                            }
                        }

                        var fee = requestParams.Count >= 2 
                            ? Fixed8.Parse(requestParams[1].AsString()) 
                            : Fixed8.Zero;

                        if (fee < Fixed8.Zero)
                        {
                            throw new RpcException(-32602, "Invalid params");
                        }

                        var changeAddress = requestParams.Count >= 3 
                            ? requestParams[2].AsString().ToScriptHash() 
                            : null;

                        var tx = this.Wallet.MakeTransaction(null, outputs, null, changeAddress, fee);
                        if (tx == null)
                        {
                            throw new RpcException(-300, "Insufficient funds");
                        }

                        var parametersContext = new ContractParametersContext(tx);
                        this.Wallet.Sign(parametersContext);
                        if (parametersContext.Completed)
                        {
                            tx.Witnesses = parametersContext.GetWitnesses();

                            this.Wallet.ApplyTransaction(tx);
                            this.system.LocalNodeActorRef.Tell(new LocalNode.Relay(tx));

                            return tx.ToJson();
                        }
                        else
                        {
                            return parametersContext.ToJson();
                        }
                    }

                case "sendrawtransaction":
                    {
                        var tx = Transaction.DeserializeFrom(requestParams[0].AsString().HexToBytes());
                        var reason = this.system.BlockchainActorRef.Ask<RelayResultReason>(tx).Result;
                        return GetRelayResult(reason);
                    }

                case "sendtoaddress":
                    if (this.Wallet == null)
                    {
                        throw new RpcException(-400, "Access denied");
                    }
                    else
                    {
                        var assetId = UIntBase.Parse(requestParams[0].AsString());
                        var descriptor = new AssetDescriptor(assetId);
                        var scriptHash = requestParams[1].AsString().ToScriptHash();
                        var value = BigDecimal.Parse(requestParams[2].AsString(), descriptor.Decimals);
                        if (value.Sign <= 0)
                        {
                            throw new RpcException(-32602, "Invalid params");
                        }

                        var fee = requestParams.Count >= 4 
                            ? Fixed8.Parse(requestParams[3].AsString()) 
                            : Fixed8.Zero;

                        if (fee < Fixed8.Zero)
                        {
                            throw new RpcException(-32602, "Invalid params");
                        }

                        var changeAddress = requestParams.Count >= 5 
                            ? requestParams[4].AsString().ToScriptHash() 
                            : null;

                        var outputs = new[]
                        {
                            new TransferOutput { AssetId = assetId,  Value = value, ScriptHash = scriptHash }
                        };

                        var tx = Wallet.MakeTransaction(null, outputs, changeAddress: changeAddress, fee: fee);
                        if (tx == null)
                        {
                            throw new RpcException(-300, "Insufficient funds");
                        }

                        var parametersContext = new ContractParametersContext(tx);
                        Wallet.Sign(parametersContext);

                        if (parametersContext.Completed)
                        {
                            tx.Witnesses = parametersContext.GetWitnesses();

                            this.Wallet.ApplyTransaction(tx);

                            var relayMessage = new LocalNode.Relay(tx);
                            this.system.LocalNodeActorRef.Tell(relayMessage);
                            return tx.ToJson();
                        }
                        else
                        {
                            return parametersContext.ToJson();
                        }
                    }

                case "submitblock":
                    {
                        var block = requestParams[0].AsString().HexToBytes().AsSerializable<Block>();
                        var relayResultReason = this.system.BlockchainActorRef.Ask<RelayResultReason>(block).Result;
                        return RpcServer.GetRelayResult(relayResultReason);
                    }

                case "validateaddress":
                    {
                        var json = new JObject();
                        UInt160 scriptHash;
                        try
                        {
                            scriptHash = requestParams[0].AsString().ToScriptHash();
                        }
                        catch
                        {
                            scriptHash = null;
                        }

                        json["address"] = requestParams[0];
                        json["isvalid"] = scriptHash != null;
                        return json;
                    }

                default:
                    throw new RpcException(-32601, "Method not found");
            }
        }

        private async Task ProcessAsync(HttpContext context)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            context.Response.Headers["Access-Control-Max-Age"] = "31536000";
            if (context.Request.Method != "GET" && context.Request.Method != "POST")
            {
                return;
            }

            JObject request = null;
            if (context.Request.Method == "GET")
            {
                var jsonrpc = (string)context.Request.Query["jsonrpc"];
                var id = (string)context.Request.Query["id"];
                var method = (string)context.Request.Query["method"];
                var parameters = (string)context.Request.Query["params"];
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(method) && !string.IsNullOrEmpty(parameters))
                {
                    try
                    {
                        parameters = Encoding.UTF8.GetString(Convert.FromBase64String(parameters));
                    }
                    catch (FormatException)
                    {
                    }

                    request = new JObject();
                    if (!string.IsNullOrEmpty(jsonrpc))
                    {
                        request["jsonrpc"] = jsonrpc;
                    }

                    request["id"] = id;
                    request["method"] = method;
                    request["params"] = JObject.Parse(parameters);
                }
            }
            else if (context.Request.Method == "POST")
            {
                using (var reader = new StreamReader(context.Request.Body))
                {
                    try
                    {
                        request = JObject.Parse(reader);
                    }
                    catch (FormatException)
                    {
                    }
                }
            }

            JObject response;
            if (request == null)
            {
                response = RpcServer.CreateErrorResponse(null, -32700, "Parse error");
            }
            else if (request is JArray array)
            {
                if (array.Count == 0)
                {
                    response = RpcServer.CreateErrorResponse(request["id"], -32600, "Invalid Request");
                }
                else
                {
                    response = array
                        .Select(p => this.ProcessRequest(context, p))
                        .Where(p => p != null)
                        .ToArray();
                }
            }
            else
            {
                response = this.ProcessRequest(context, request);
            }

            if (response == null || (response as JArray)?.Count == 0)
            {
                return;
            }

            context.Response.ContentType = "application/json-rpc";
            await context.Response.WriteAsync(response.ToString(), Encoding.UTF8);
        }

        private JObject ProcessRequest(HttpContext context, JObject request)
        {
            if (!request.ContainsProperty("id"))
            {
                return null;
            }

            if (!request.ContainsProperty("method") 
                || !request.ContainsProperty("params") 
                || !(request["params"] is JArray))
            {
                return CreateErrorResponse(request["id"], -32600, "Invalid Request");
            }

            JObject result = null;
            try
            {
                var methodName = request["method"].AsString();
                var requestParams = (JArray)request["params"];
                foreach (var plugin in Plugin.RpcPlugins)
                {
                    result = plugin.OnProcess(context, methodName, requestParams);
                    if (result != null)
                    {
                        break;
                    }
                }

                if (result == null)
                {
                    result = this.Process(methodName, requestParams);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                return CreateErrorResponse(request["id"], ex.HResult, ex.Message, ex.StackTrace);
#else
                return CreateErrorResponse(request["id"], ex.HResult, ex.Message);
#endif
            }

            var response = RpcServer.CreateResponse(request["id"]);
            response["result"] = result;
            return response;
        }
    }
}
