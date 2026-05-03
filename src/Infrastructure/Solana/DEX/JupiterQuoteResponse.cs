using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Solana.DEX
{
    internal sealed class JupiterQuoteResponse
    {
        [JsonPropertyName("inputMint")]
        public string InputMint { get; init; } = default!;

        [JsonPropertyName("inAmount")]
        public string InAmount { get; init; } = default!;

        [JsonPropertyName("outputMint")]
        public string OutputMint { get; init; } = default!;

        [JsonPropertyName("outAmount")]
        public string OutAmount { get; init; } = default!;

        [JsonPropertyName("priceImpactPct")]
        public string? PriceImpactPct { get; init; }

        [JsonPropertyName("swapUsdValue")]
        public string? SwapUsdValue { get; init; }

        [JsonPropertyName("routePlan")]
        public List<RoutePlan>? RoutePlan { get; init; }
    }
}
