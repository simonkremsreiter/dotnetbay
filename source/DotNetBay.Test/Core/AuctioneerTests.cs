﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using DotNetBay.Core.Execution;
using DotNetBay.Data.FileStorage;
using DotNetBay.Model;

using NUnit.Framework;

namespace DotNetBay.Test.Core
{
    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "This is a Testclass")]
    public class AuctioneerTests
    {
        [TestCase]
        public void Auction_HasNewerButLowerBid_HasNoImpact()
        {
            var repo = new InMemoryMainRepository();
            var auctioneer = new Auctioneer(repo);

            var auction = CreateAndStoreAuction(repo, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
            AddInitialBidToAuction(repo, auction);
            
            auctioneer.DoAllWork();

            var bidder2 = new Member() { Name = "Bidder2", UniqueId = Guid.NewGuid().ToString() };
            repo.Add(bidder2);
            repo.Add(new Bid() { ReceivedOnUtc = DateTime.UtcNow, Bidder = bidder2, Amount = 51, Auction = auction });

            auctioneer.DoAllWork();

            Assert.AreEqual(2, auction.Bids.Count);
            Assert.AreEqual(60, auction.CurrentPrice);
        }

        [TestCase]
        public void Auction_GetsNewerButHigherBid_PriceIsAffected()
        {
            var repo = new InMemoryMainRepository();
            var auctioneer = new Auctioneer(repo);

            var auction = CreateAndStoreAuction(repo, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
            AddInitialBidToAuction(repo, auction);

            auctioneer.DoAllWork();

            var bidder2 = new Member() { Name = "Bidder2", UniqueId = Guid.NewGuid().ToString() };
            repo.Add(bidder2);
            repo.Add(new Bid() { ReceivedOnUtc = DateTime.UtcNow, Bidder = bidder2, Amount = 70, Auction = auction });

            auctioneer.DoAllWork();

            Assert.AreEqual(2, auction.Bids.Count);
            Assert.AreEqual(70, auction.CurrentPrice);
        }

        [TestCase]
        public void Auction_GetsOlderButLowerBid_HasNoImpact()
        {
            var repo = new InMemoryMainRepository();
            var auctioneer = new Auctioneer(repo);

            var auction = CreateAndStoreAuction(repo, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
            AddInitialBidToAuction(repo, auction);

            auctioneer.DoAllWork();

            var bidder2 = new Member() { Name = "Bidder2", UniqueId = Guid.NewGuid().ToString() };
            repo.Add(bidder2);
            repo.Add(new Bid() { ReceivedOnUtc = DateTime.UtcNow.AddMinutes(-10), Bidder = bidder2, Amount = 51, Auction = auction });

            auctioneer.DoAllWork();

            Assert.AreEqual(2, auction.Bids.Count);
            Assert.AreEqual(60, auction.CurrentPrice);
        }

        [TestCase]
        [ExpectedException(typeof(ApplicationException))]
        public void Auction_GetsOlderButHigherBid_FailsWithException()
        {
            var repo = new InMemoryMainRepository();
            var auctioneer = new Auctioneer(repo);

            var auction = CreateAndStoreAuction(repo, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
            AddInitialBidToAuction(repo, auction);

            auctioneer.DoAllWork();

            var bidder2 = new Member() { Name = "Bidder2", UniqueId = Guid.NewGuid().ToString() };
            repo.Add(bidder2);
            repo.Add(new Bid() { ReceivedOnUtc = DateTime.UtcNow.AddMinutes(-10), Bidder = bidder2, Amount = 70, Auction = auction });

            auctioneer.DoAllWork();
        }

        [TestCase]
        public void Auction_EndTimeHasArrived_AuctionGetsClosed()
        {
            var repo = new InMemoryMainRepository();
            var auctioneer = new Auctioneer(repo);

            var auction = CreateAndStoreAuction(repo, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
            
            auctioneer.DoAllWork();

            Assert.IsFalse(auction.IsClosed);

            // Turn back the time
            auction.EndDateTimeUtc = DateTime.UtcNow;

            auctioneer.DoAllWork();

            Assert.IsTrue(auction.IsClosed);
        }

        [TestCase]
        public void Auction_HasOneBidAndGetsClose_TheBidderShouldBeTheWinner()
        {
            var repo = new InMemoryMainRepository();
            var auctioneer = new Auctioneer(repo);

            var auction = CreateAndStoreAuction(repo, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

            auctioneer.DoAllWork();

            var bidder2 = new Member() { Name = "Bidder2", UniqueId = Guid.NewGuid().ToString() };
            repo.Add(bidder2);
            repo.Add(new Bid() { ReceivedOnUtc = DateTime.UtcNow, Bidder = bidder2, Amount = 70, Auction = auction });

            // Turn back the time
            auction.EndDateTimeUtc = DateTime.UtcNow;

            auctioneer.DoAllWork();

            Assert.IsTrue(auction.IsClosed);
            Assert.AreEqual(auction.Winner, bidder2);
        }

        [TestCase]
        public void Auction_WhenClosed_EventIsRaised()
        {
            var repo = new InMemoryMainRepository();
            var auctioneer = new Auctioneer(repo);

            var auction = CreateAndStoreAuction(repo, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

            AuctionEventArgs raisedArgs = null;
            auctioneer.AuctionClosed += (sender, args) => raisedArgs = args;

            // Turn back the time
            auction.EndDateTimeUtc = DateTime.UtcNow;

            auctioneer.DoAllWork();

            Assert.NotNull(raisedArgs);
            Assert.NotNull(raisedArgs.Auction);
            Assert.NotNull(raisedArgs.IsSuccessful);
        }

        [TestCase]
        public void Bid_WhenAccepted_EventIsRaised()
        {
            var repo = new InMemoryMainRepository();
            var auctioneer = new Auctioneer(repo);

            var auction = CreateAndStoreAuction(repo, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

            ProcessedBidEventArgs raisedArgs = null;
            auctioneer.BidAccepted += (sender, args) => raisedArgs = args;

            AddInitialBidToAuction(repo, auction);

            auctioneer.DoAllWork();

            Assert.NotNull(raisedArgs);
            Assert.NotNull(raisedArgs.Auction);
            Assert.NotNull(raisedArgs.Bid);
        }

        [TestCase]
        public void Bid_WhenDeclined_EventIsRaised()
        {
            var repo = new InMemoryMainRepository();
            var auctioneer = new Auctioneer(repo);

            var auction = CreateAndStoreAuction(repo, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
            AddInitialBidToAuction(repo, auction);

            auctioneer.DoAllWork();

            ProcessedBidEventArgs raisedArgs = null;
            auctioneer.BidDeclined += (sender, args) => raisedArgs = args;

            var bidder2 = new Member() { Name = "Bidder2", UniqueId = Guid.NewGuid().ToString() };
            repo.Add(bidder2);
            repo.Add(new Bid() { ReceivedOnUtc = DateTime.UtcNow, Bidder = bidder2, Amount = 51, Auction = auction });
            
            auctioneer.DoAllWork();

            Assert.NotNull(raisedArgs);
            Assert.NotNull(raisedArgs.Auction);
            Assert.NotNull(raisedArgs.Bid);
        }

        private static void AddInitialBidToAuction(InMemoryMainRepository repo, Auction auction)
        {
            var bidder = new Member() { Name = "Bidder1", UniqueId = Guid.NewGuid().ToString() };
            repo.Add(bidder);

            repo.Add(new Bid() { ReceivedOnUtc = DateTime.UtcNow, Auction = auction, Amount = auction.StartPrice + 10, Bidder = bidder });
        }

        private static Auction CreateAndStoreAuction(InMemoryMainRepository repo, DateTime startDateTimeUtc, DateTime endDateTimeUtc)
        {
            var seller = new Member() { Name = "Seller", UniqueId = Guid.NewGuid().ToString() };
            var auction = new Auction() { Title = "TestAuction", Seller = seller, StartPrice = 50, StartDateTimeUtc = startDateTimeUtc, EndDateTimeUtc = endDateTimeUtc };

            repo.Add(seller);
            repo.Add(auction);

            Assert.AreEqual(1, repo.GetAuctions().Count());
            Assert.AreEqual(1, repo.GetMembers().Count());

            return auction;
        }
    }
}
