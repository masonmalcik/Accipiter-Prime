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
            ["JTO"] = "jtojtomepa8beP8AuQc6eXt5FriJwfFMwQx2v2f9mCL",
            ["WIF"] = "EKpQGSJtjMFqKZ9KQanSqYXRcF8fBopzLHYxdM65zcjm",
            ["RAY"] = "4k3Dyjzvzp8eMZWUXbBCjEvwSkkk59S5iCNLY3QrkX6R",
            ["ORCA"] = "orcaEKTdK7LKz57vaAYr9QeNsVEPfiu6QeMU1kektZE"
        };

        // Jupiter uses 6 decimals for USDC, 9 for SOL
        private static readonly Dictionary<string, int> TokenDecimals = new()
        {
            ["USDC"] = 6,
            ["SOL"] = 9,
            ["ETH"] = 8,
            ["BTC"] = 6,
            ["BONK"] = 5,
            ["JTO"] = 9,
            ["WIF"] = 6,
            ["RAY"] = 6,
            ["ORCA"] = 6
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
                    await Task.Delay(1000, ct); // increase from 500ms to 1000ms
                }
                catch (HttpRequestException ex) when ((int?)ex.StatusCode == 429)
                {
                    _logger.LogWarning("Jupiter rate limit hit for {Base}/{Quote} — backing off 15s",
                        pair.BaseToken, pair.QuoteToken);
                    await Task.Delay(15000, ct);
                }
                catch (HttpRequestException ex) when ((int?)ex.StatusCode == 500)
                {
                    _logger.LogWarning("Jupiter 500 error for {Base}/{Quote} — skipping | {Message}",
                        pair.BaseToken, pair.QuoteToken, ex.Message);
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

            var rawInputAmount = (long)(inputAmountUSDC * (decimal)Math.Pow(10, inputDecimals));

            var url = $"{_jupiterApiUrl}/quote" +
                      $"?inputMint={quoteMint}" +
                      $"&outputMint={baseMint}" +
                      $"&amount={rawInputAmount}" +
                      $"&slippageBps=50" +
                      $"&restrictIntermediateTokens=true";

            _logger.LogInformation("Jupiter URL being called: {Url}", url);
            _logger.LogInformation("Jupiter base URL from config: {Base}", _jupiterApiUrl);
            _logger.LogInformation("Requesting quote for {Base}/{Quote} | URL: {Url}",
                pair.BaseToken, pair.QuoteToken, url);

            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await _http.GetAsync(url, ct);
                httpResponse.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Jupiter API call failed: {Message} | URL: {Url}",
                    ex.Message, url);
                return [];
            }

            var response = await httpResponse.Content
                .ReadFromJsonAsync<JupiterQuoteResponse>(ct);

            if (response is null)
                return [];

            // Convert output amount using correct decimals
            var bestOutputRaw = long.Parse(response.OutAmount);
            var bestOutputAmount = bestOutputRaw / (decimal)Math.Pow(10, outputDecimals);

            _logger.LogDebug("Got quote for {Base}/{Quote} — output: {Output}",
                pair.BaseToken, pair.QuoteToken, bestOutputAmount);

            // Only return the single best Jupiter aggregate quote
            // Do NOT use individual route plan legs as separate rates
            // Those represent internal multi-hop routing, not real executable direct swap rates
            return
            [
                new DexQuote
                {
                    Dex            = "Jupiter",
                    Pair           = pair,
                    InputAmount    = inputAmountUSDC,
                    OutputAmount   = bestOutputAmount,
                    PriceImpactBps = ParsePriceImpact(response.PriceImpactPct)
                }
            ];
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
