using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Neo.Consensus;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using System;

namespace Neo.UnitTests
{

    [TestClass]
    public class ConsensusTests : TestKit
    {
        [TestCleanup]
        public void Cleanup()
        {
            Shutdown();
        }

        [TestMethod]
        public void ConsensusService_Primary_Sends_PrepareRequest_After_OnStart()
        {
            TestProbe subscriber = CreateTestProbe();

            var mockConsensusContext = new Mock<IConsensusContext>();

            // context.Reset(): do nothing
            //mockConsensusContext.Setup(mr => mr.Reset()).Verifiable(); // void
            mockConsensusContext.SetupGet(mr => mr.MyIndex).Returns(2); // MyIndex == 2
            mockConsensusContext.SetupGet(mr => mr.BlockIndex).Returns(2);
            mockConsensusContext.SetupGet(mr => mr.PrimaryIndex).Returns(2);
            mockConsensusContext.SetupGet(mr => mr.ViewNumber).Returns(0);
            mockConsensusContext.SetupProperty(mr => mr.Nonce);
            mockConsensusContext.SetupProperty(mr => mr.NextConsensus);
            mockConsensusContext.Object.NextConsensus = UInt160.Zero;
            mockConsensusContext.Setup(mr => mr.GetPrimaryIndex(It.IsAny<byte>())).Returns(2);
            mockConsensusContext.SetupProperty(mr => mr.State);  // allows get and set to update mock state on Initialize method
            mockConsensusContext.Object.State = ConsensusStates.Initial;

            int timeIndex = 0;
            var timeValues = new[] {
              new DateTime(1968, 06, 01, 0, 0, 15, DateTimeKind.Utc), // For tests here
              new DateTime(1968, 06, 01, 0, 0, 1, DateTimeKind.Utc),  // For receiving block
              new DateTime(1968, 06, 01, 0, 0, 15, DateTimeKind.Utc), // For Initialize
              new DateTime(1968, 06, 01, 0, 0, 15, DateTimeKind.Utc), // unused
              new DateTime(1968, 06, 01, 0, 0, 15, DateTimeKind.Utc)  // unused
          };

            Console.WriteLine($"time 0: {timeValues[0].ToString()} 1: {timeValues[1].ToString()} 2: {timeValues[2].ToString()} 3: {timeValues[3].ToString()}");

            //mockConsensusContext.Object.block_received_time = new DateTime(1968, 06, 01, 0, 0, 1, DateTimeKind.Utc);
            //mockConsensusContext.Setup(mr => mr.GetUtcNow()).Returns(new DateTime(1968, 06, 01, 0, 0, 15, DateTimeKind.Utc));

            var timeMock = new Mock<TimeProvider>();
            timeMock.SetupGet(tp => tp.UtcNow).Returns(() => timeValues[timeIndex])
                                              .Callback(() => timeIndex++);
            //new DateTime(1968, 06, 01, 0, 0, 15, DateTimeKind.Utc));
            TimeProvider.Current = timeMock.Object;

            //public void Log(string message, LogLevel level)
            // TODO: create ILogPlugin for Tests
            /*
            mockConsensusContext.Setup(mr => mr.Log(It.IsAny<string>(), It.IsAny<LogLevel>()))
                         .Callback((string message, LogLevel level) => {
                                         Console.WriteLine($"CONSENSUS LOG: {message}");
                                                                   }
                                  );
             */

            // Creating proposed block
            Header header = new Header();
            TestUtils.SetupHeaderWithValues(header, UInt256.Zero, out UInt256 merkRootVal, out UInt160 val160, out uint timestampVal, out uint indexVal, out ulong consensusDataVal, out Witness scriptVal);
            header.Size.Should().Be(109);

            Console.WriteLine($"header {header} hash {header.Hash} timstamp {timestampVal}");

            timestampVal.Should().Be(4244941696); //1968-06-01 00:00:00
            TimeProvider.Current.UtcNow.ToTimestamp().Should().Be(4244941711); //1968-06-01 00:00:15
                                                                               // check basic ConsensusContext
            mockConsensusContext.Object.MyIndex.Should().Be(2);
            //mockConsensusContext.Object.block_received_time.ToTimestamp().Should().Be(4244941697); //1968-06-01 00:00:01

            MinerTransaction minerTx = new MinerTransaction
            {
                Attributes = new TransactionAttribute[0],
                Inputs = new CoinReference[0],
                Outputs = new TransactionOutput[0],
                Witnesses = new Witness[0],
                Nonce = 42
            };

            PrepareRequest prep = new PrepareRequest
            {
                Nonce = mockConsensusContext.Object.Nonce,
                NextConsensus = mockConsensusContext.Object.NextConsensus,
                TransactionHashes = new UInt256[0],
                MinerTransaction = minerTx, //(MinerTransaction)Transactions[TransactionHashes[0]],
                Signature = new byte[64]//Signatures[MyIndex]
            };

            ConsensusMessage mprep = prep;
            byte[] prepData = mprep.ToArray();

            ConsensusPayload prepPayload = new ConsensusPayload
            {
                Version = 0,
                PrevHash = mockConsensusContext.Object.PreviousBlockHash,
                BlockIndex = mockConsensusContext.Object.BlockIndex,
                ValidatorIndex = (ushort)mockConsensusContext.Object.MyIndex,
                Timestamp = mockConsensusContext.Object.Timestamp,
                Data = prepData
            };

            mockConsensusContext.Setup(mr => mr.MakePrepareRequest()).Returns(prepPayload);

            // ============================================================================
            //                      creating ConsensusService actor
            // ============================================================================

            TestActorRef<ConsensusService> actorConsensus = ActorOfAsTestActorRef<ConsensusService>(
                                     Akka.Actor.Props.Create(() => new ConsensusService(subscriber, subscriber, mockConsensusContext.Object))
                                     );

            Console.WriteLine("will trigger OnPersistCompleted!");

            var block = new Block
            {
                Version = header.Version,
                PrevHash = header.PrevHash,
                MerkleRoot = header.MerkleRoot,
                Timestamp = header.Timestamp,
                Index = header.Index,
                ConsensusData = header.ConsensusData,
                NextConsensus = header.NextConsensus
            };

            actorConsensus.Tell(new Blockchain.PersistCompleted(block));

            //Console.WriteLine("will start consensus!");
            //actorConsensus.Tell(new ConsensusService.Start());

            Console.WriteLine("OnTimer should expire!");
            Console.WriteLine("Waiting for subscriber message!");

            var answer = subscriber.ExpectMsg<LocalNode.SendDirectly>();
            Console.WriteLine($"MESSAGE 1: {answer}");
            //var answer2 = subscriber.ExpectMsg<LocalNode.SendDirectly>(); // expects to fail!

            // ============================================================================
            //                      finalize ConsensusService actor
            // ============================================================================

            //Thread.Sleep(4000);
            Sys.Stop(actorConsensus);
            TimeProvider.ResetToDefault();

            Assert.AreEqual(1, 1);
        }
    }
}
