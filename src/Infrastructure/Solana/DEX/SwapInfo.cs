using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Solana.DEX
{
    internal sealed class SwapInfo
    {
        [JsonPropertyName("ammKey")]
        public string? AmmKey { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("inputMint")]
        public string? InputMint { get; init; }

        [JsonPropertyName("outputMint")]
        public string? OutputMint { get; init; }

        [JsonPropertyName("inAmount")]
        public string? InAmount { get; init; }

        [JsonPropertyName("outAmount")]
        public string? OutAmount { get; init; }

        [JsonPropertyName("feeAmount")]
        public string? FeeAmount { get; init; }
    }
}
