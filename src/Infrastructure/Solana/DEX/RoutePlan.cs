using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Solana.DEX
{
    internal sealed class RoutePlan
    {
        [JsonPropertyName("swapInfo")]
        public SwapInfo? SwapInfo { get; init; }

        [JsonPropertyName("percent")]
        public int? Percent { get; init; }
    }
}
