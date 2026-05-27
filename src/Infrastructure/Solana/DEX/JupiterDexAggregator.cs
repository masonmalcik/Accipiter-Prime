using Accipiter.Core.Domain.Interfaces;
using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Solana.DEX
{
    public sealed class JupiterDexAggregator : IDexPriceAggregator
    {
        private readonly HttpClient _http;
        private readonly ILogger<JupiterDexAggregator> _logger;
        private readonly string _jupiterApiUrl;

        // Token mint addresses (Solana mainnet)
        private static readonly Dictionary<string, string> TokenMints = new()
        {
            ["USDC"] = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v",
            ["SOL"] = "So11111111111111111111111111111111111111112",
            ["ETH"] = "7vfCXTUXx5WJV5JADk17DUJ4ksgau7utNKj4b963voxs",
            ["BTC"] = "9n4nbM75f5Ui33ZbPYXn59EwSgE8CGsHtAeTH5YFeJ9E",
            ["BONK"] = "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263",
            ["JTO"] = "jtojtomepa8beP8AuQc6eXt5FriJwfFMwQx2v2f9mCL"
        };

        // Jupiter uses 6 decimals for USDC, 9 for SOL
        private static readonly Dictionary<string, int> TokenDecimals = new()
        {
            ["USDC"] = 6,
            ["SOL"] = 9,
            ["ETH"] = 8,
            ["BTC"] = 6,
            ["BONK"] = 5,
            ["JTO"] = 9
        };

        public JupiterDexAggregator(
            HttpClient http,
            IConfiguration config,
            ILogger<JupiterDexAggregator> logger)
        {
            _logger = logger; // ← must be first before any logging calls

            _http = http;
            _http.DefaultRequestHeaders.Add("User-Agent", "Accipiter/1.0");
            _http.DefaultRequestHeaders.Add("Accept", "application/json");

            var apiKey = config["Dex:JupiterApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
                _logger.LogInformation("Jupiter API key loaded successfully");
            }
            else
            {
                _logger.LogWarning("No Jupiter API key found — using free tier");
            }

            _jupiterApiUrl = config["Dex:JupiterApiUrl"]
                ?? "https://lite-api.jup.ag/swap/v1";
        }

        public async Task<IReadOnlyList<DexQuote>> GetQuotesAsync(
            IReadOnlyList<TokenPair> pairs,
            decimal inputAmountUSDC,
            CancellationToken ct = default)
        {
            var quotes = new List<DexQuote>();

            foreach (var pair in pairs)
            {
                try
                {
                    var pairQuotes = await GetQuotesForPairAsync(pair, inputAmountUSDC, ct);
                    quotes.AddRange(pairQuotes);
                    await Task.Delay(500, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to fetch quotes for {Base}/{Quote}", pair.BaseToken, pair.QuoteToken);
                }
            }

            return quotes;
        }

        private async Task<IReadOnlyList<DexQuote>> GetQuotesForPairAsync(
            TokenPair pair,
            decimal inputAmountUSDC,
            CancellationToken ct)
        {
            if (!TokenMints.TryGetValue(pair.BaseToken, out var baseMint) ||
                !TokenMints.TryGetValue(pair.QuoteToken, out var quoteMint))
            {
                _logger.LogWarning("Unknown token in pair {Base}/{Quote} — skipping",
                    pair.BaseToken, pair.QuoteToken);
                return [];
            }

            var inputDecimals = TokenDecimals.GetValueOrDefault(pair.QuoteToken, 6);
            var outputDecimals = TokenDecimals.GetValueOrDefault(pair.BaseToken, 9);

            // Convert USDC decimal amount to raw integer units
            var rawInputAmount = (long)(inputAmountUSDC * (decimal)Math.Pow(10, inputDecimals));

            // Jupiter /quote returns best route across all DEXes it aggregates,
            // plus the individual route plans broken out by DEX.
            // We request the top 3 route plans so we can compare DEX prices.
            var url = $"{_jupiterApiUrl}/quote" +
                      $"?inputMint={quoteMint}" +
                      $"&outputMint={baseMint}" +
                      $"&amount={rawInputAmount}" +
                      $"&slippageBps=50" +
                      $"&restrictIntermediateTokens=true";

            _logger.LogDebug("Jupiter quote request: {Url}", url);

            _logger.LogInformation("Jupiter URL being called: {Url}", url);
            _logger.LogInformation("Jupiter base URL from config: {Base}", _jupiterApiUrl);

            var response = await _http.GetFromJsonAsync<JupiterQuoteResponse>(url, ct);

            if (response is null)
                return [];

            var results = new List<DexQuote>();

            // Best overall quote — labelled as "Jupiter (Best)"
            var bestOutputRaw = long.Parse(response.OutAmount);
            var bestOutputAmount = bestOutputRaw / (decimal)Math.Pow(10, outputDecimals);

            results.Add(new DexQuote
            {
                Dex = "Jupiter",
                Pair = pair,
                InputAmount = inputAmountUSDC,
                OutputAmount = bestOutputAmount,
                PriceImpactBps = ParsePriceImpact(response.PriceImpactPct)
            });

            // Break out individual DEX legs from route plans for cross-DEX comparison
            if (response.RoutePlan is { Count: > 0 })
            {
                foreach (var route in response.RoutePlan)
                {
                    var dexLabel = route.SwapInfo?.Label ?? "Unknown";
                    if (dexLabel == "Jupiter") continue; // already added above

                    var routeOutputRaw = long.TryParse(route.SwapInfo?.OutAmount, out var r) ? r : 0;
                    var routeOutputAmount = routeOutputRaw / (decimal)Math.Pow(10, outputDecimals);

                    if (routeOutputAmount <= 0) continue;

                    results.Add(new DexQuote
                    {
                        Dex = dexLabel,
                        Pair = pair,
                        InputAmount = inputAmountUSDC,
                        OutputAmount = routeOutputAmount,
                        PriceImpactBps = ParsePriceImpact(response.PriceImpactPct)
                    });
                }
            }

            _logger.LogDebug("Got {Count} quotes for {Base}/{Quote}",
                results.Count, pair.BaseToken, pair.QuoteToken);

            return results;
        }

        private static decimal ParsePriceImpact(string? pct)
        {
            if (string.IsNullOrWhiteSpace(pct)) return 0m;
            return decimal.TryParse(pct, out var val)
                ? val * 100  // convert fraction to bps (0.001 → 0.1 bps)
                : 0m;
        }
    }
}
