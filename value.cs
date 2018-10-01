using System.Collections.Concurrent;

namespace QuantConnect.Algorithm.CSharp {
  public class Demo : QCAlgorithm {

    private DateTime _startDate = new DateTime(2006, 02, 04);
    private DateTime _endDate = new DateTime(2018, 09, 10);
    private List<DateTime> _rebalancingDates = new List<DateTime>();
    private bool _isRebalancing = false;
    private bool _isPlacingOrders = false;

    private List<Symbol> _nextPortfolio = new List<Symbol>();

    public override void Initialize() {
      UniverseSettings.Leverage = 1.0m;
      UniverseSettings.Resolution = Resolution.Daily;

      SetStartDate(_startDate);
      SetEndDate(_endDate);
      SetCash(100*1000);

      this._rebalancingDates = GenerateRebalancingDates(this._startDate, this._endDate);

      AddUniverse(
        coarse => {
          _isRebalancing = false;
          _isPlacingOrders = false;
          if (_rebalancingDates.Count > 0 && Time > _rebalancingDates.ElementAt(0)) {
            Debug("1 " + Time);
            _rebalancingDates.RemoveAt(0);
            _isRebalancing = true;
            _isPlacingOrders = true;

            return (from cf in coarse
              where cf.Market == Market.USA && cf.DollarVolume > 1000000m && cf.Price > 5m
              // orderby cf.DollarVolume descending
              select cf.Symbol
            );
          }
          return _nextPortfolio;
        },
        fine => {
          if (_isRebalancing) {
            ICollection<Tuple<Symbol, decimal>> symbols = new List<Tuple<Symbol, decimal>>();
            foreach (var ff in fine) {
              if (ff.ValuationRatios.RatioPE5YearAverage > 0 && ff.ValuationRatios.PERatio > 0) {
                decimal peRatio = ff.ValuationRatios.PERatio / ff.ValuationRatios.RatioPE5YearAverage;
                if (peRatio < 0.7m) {
                  //&& ff.OperationRatios.ROIC.Value > 0.2m && ff.ValuationRatios.ForwardDividendYield > 0.04m
                  symbols.Add(Tuple.Create(ff.Symbol, ff.OperationRatios.ROIC.Value));
                }
              }
            }
            _nextPortfolio = symbols
              .OrderByDescending(s => s.Item2)
              .Take(10)
              .Select(s => s.Item1)
              .ToList();
          }
          return _nextPortfolio;
        }
      );
    }

    public void Rebalance(Universe universe, List<Symbol> newPortfolio) {
      List<Symbol> portfolio = GetPortfolioSymbols();
      List<Symbol> sellList = this.minus(portfolio, newPortfolio);
      List<Symbol> buyList = this.minus(newPortfolio, portfolio);

      int diff = sellList.Count() - buyList.Count();
      while (diff > 0) {
        sellList.RemoveAt(0);
        diff--;
      }

      foreach (var symbol in sellList) {
        Security security = universe.Members[symbol];
        DateTime nextOpen = security.Exchange.Hours.GetNextMarketOpen(new DateTime(Time.Year, Time.Month, Time.Day), false);

        Schedule.On(
          DateRules.On(nextOpen.Year, nextOpen.Month, nextOpen.Day),
          TimeRules.AfterMarketOpen(symbol, 60),
          () => {
            //Debug("sold " + symbol);
            Liquidate(symbol);
          }
        );
      }

      foreach (var symbol in buyList) {
        Security security = universe.Members[symbol];
        DateTime nextOpen = security.Exchange.Hours.GetNextMarketOpen(new DateTime(Time.Year, Time.Month, Time.Day), false);

        Schedule.On(
          DateRules.On(nextOpen.Year, nextOpen.Month, nextOpen.Day),
          TimeRules.AfterMarketOpen(symbol, 90),
          () => {
            //Debug("bought " + symbol);
            SetHoldings(symbol, 0.95m / newPortfolio.Count);
          }
        );
      }
    }

    // I use on security changed to get access to the universe's members
    public override void OnSecuritiesChanged(SecurityChanges changes) {
      if (changes != SecurityChanges.None) {
        Rebalance(this.UniverseManager.ElementAt(0).Value, this._nextPortfolio);
      }
    }

    public List<DateTime> GenerateRebalancingDates(DateTime startDate, DateTime endDate) {
      List<DateTime> dates = new List<DateTime>();

      DateTime currentDate = startDate;
      while (currentDate < endDate) {
        dates.Add(currentDate);
        currentDate = currentDate.AddYears(1);
      }

      return dates;
    }

    private List<Symbol> minus(List<Symbol> l1, List<Symbol> l2) {
      List<Symbol> result = new List<Symbol>();
      foreach (var s1 in l1) {
        if (!l2.Contains(s1)) {
          result.Add(s1);
        }
      }
      return result;
    }

    public List<Symbol> GetPortfolioSymbols() {
      List<Symbol> symbols = new List<Symbol>();
      foreach (var security in Portfolio.Values) {
        if (security.Invested) {
          symbols.Add(security.Symbol);
        }
      }
      return symbols;
    }

    private String logList(List<Symbol> l) {
      String portfolioSymbols = "";
      foreach (var symbol in l) {
        portfolioSymbols += " " + symbol;
      }
      return portfolioSymbols;
    }
  }
}
