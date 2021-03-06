﻿using System;
using System.Linq;

using DotNetBay.Interfaces;

namespace DotNetBay.Core.Execution
{
    public class Auctioneer : IAuctioneer
    {
        private readonly IMainRepository repository;

        public Auctioneer(IMainRepository repository)
        {
            this.repository = repository;
        }

        #region Events

        public event EventHandler<ProcessedBidEventArgs> BidDeclined;

        public event EventHandler<ProcessedBidEventArgs> BidAccepted;

        public event EventHandler<AuctionEventArgs> AuctionClosed;

        #endregion

        public void DoAllWork()
        {
            this.ProcessOpenBids();

            this.CloseFinishedAuctions();
        }

        #region Event Invocation

        protected virtual void OnBidDeclined(ProcessedBidEventArgs e)
        {
            var handler = this.BidDeclined;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnBidAccepted(ProcessedBidEventArgs e)
        {
            var handler = this.BidAccepted;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnAuctionClosed(AuctionEventArgs e)
        {
            var handler = this.AuctionClosed;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        #endregion

        #region The Magic itself

        private void ProcessOpenBids()
        {
            // Process all auctions with open bids
            var openAuctions = this.repository.GetAuctions().Where(a => a.Bids.Any(b => b.Accepted == null));

            foreach (var auction in openAuctions)
            {
                var openBids = auction.Bids.Where(b => b.Accepted == null).OrderBy(b => b.ReceivedOnUtc).ToList();

                foreach (var bid in openBids)
                {
                    if (bid.Amount > auction.CurrentPrice)
                    {
                        if (auction.ActiveBid != null && bid.ReceivedOnUtc < auction.ActiveBid.ReceivedOnUtc)
                        {
                            throw new ApplicationException("Cannot handle higher bids which where look like coming from history!");
                        }

                        bid.Accepted = true;
                        auction.ActiveBid = bid;
                        auction.CurrentPrice = bid.Amount;
                        this.OnBidAccepted(new ProcessedBidEventArgs { Bid = bid, Auction = auction });
                    }
                    else
                    {
                        bid.Accepted = false;
                        this.OnBidDeclined(new ProcessedBidEventArgs { Bid = bid, Auction = auction });
                    }
                }
            }

            this.repository.SaveChanges();
        }

        private void CloseFinishedAuctions()
        {
            // Process all auctions which should be closed
            var auctionsToClose =
                this.repository.GetAuctions().Where(a => !a.IsClosed && a.EndDateTimeUtc <= DateTime.UtcNow).ToList();

            foreach (var auction in auctionsToClose)
            {
                // Skip any auctions with not processed bids
                if (auction.Bids.Any(b => b.Accepted == null))
                {
                    continue;
                }

                if (auction.Bids.Any())
                {
                    auction.Winner = auction.ActiveBid.Bidder;
                }

                auction.IsClosed = true;
                auction.CloseDateTimeUtc = DateTime.UtcNow;

                this.repository.SaveChanges();

                this.OnAuctionClosed(
                    new AuctionEventArgs() { Auction = auction, IsSuccessful = auction.Winner != null });
            }
        }

        #endregion
    }
}