// ============================================================
// Accipiter.Application / Orchestration / CircuitBreaker.cs
// ============================================================
using Accipiter.Core.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accipiter.Application.Orchestration;

/// <summary>
/// Circuit breaker that monitors trading health and automatically
/// halts execution when danger thresholds are breached.
///
/// Three states:
///   Closed  — normal operation, trades flowing
///   Open    — halted, all trade attempts rejected
///   HalfOpen — testing recovery after a cooldown period
/// </summary>
public sealed class CircuitBreaker
{
    private CircuitState _state = CircuitState.Closed;
    private DateTimeOffset? _openedAt;
    private int _consecutiveFailures = 0;
    private int _consecutiveReverts = 0;
    private decimal _sessionLossUSDC = 0m;
    private readonly Lock _lock = new();

    private readonly CircuitBreakerOptions _options;
    private readonly ILogger<CircuitBreaker> _logger;

    public CircuitState State => _state;
    public bool IsOpen => _state == CircuitState.Open;
    public bool AllowTrading => _state != CircuitState.Open;

    public CircuitBreaker(
        IOptions<CircuitBreakerOptions> options,
        ILogger<CircuitBreaker> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Call after every successful trade execution.
    /// </summary>
    public void RecordSuccess(decimal profitUSDC)
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _consecutiveReverts = 0;

            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Closed;
                _logger.LogInformation(
                    "Circuit breaker CLOSED — recovery confirmed after successful trade");
            }

            _logger.LogDebug("Circuit breaker: success recorded | profit: {Profit:C}",
                profitUSDC);
        }
    }

    /// <summary>
    /// Call after every failed or reverted trade.
    /// </summary>
    public void RecordFailure(string reason, decimal lossUSDC = 0m)
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            _sessionLossUSDC += lossUSDC;

            if (lossUSDC > 0)
                _consecutiveReverts++;

            _logger.LogWarning(
                "Circuit breaker: failure recorded | reason: {Reason} | " +
                "consecutive: {Count} | session loss: {Loss:C}",
                reason, _consecutiveFailures, _sessionLossUSDC);

            EvaluateTrip(reason);
        }
    }

    /// <summary>
    /// Call at the start of each tick to check if trading is allowed.
    /// Handles automatic recovery after cooldown period.
    /// </summary>
    public bool CheckAndAllow()
    {
        lock (_lock)
        {
            if (_state == CircuitState.Closed)
                return true;

            if (_state == CircuitState.Open)
            {
                var elapsed = DateTimeOffset.UtcNow - _openedAt!.Value;

                if (elapsed >= TimeSpan.FromMinutes(_options.CooldownMinutes))
                {
                    _state = CircuitState.HalfOpen;
                    _logger.LogInformation(
                        "Circuit breaker HALF-OPEN — attempting recovery after {Min} min cooldown",
                        _options.CooldownMinutes);
                    return true; // allow one test trade
                }

                _logger.LogDebug(
                    "Circuit breaker OPEN — {Remaining:F0}s remaining in cooldown",
                    (_options.CooldownMinutes * 60) - elapsed.TotalSeconds);
                return false;
            }

            // HalfOpen — allow through, result will close or re-open
            return true;
        }
    }

    public CircuitBreakerStatus GetStatus() => new()
    {
        State = _state,
        ConsecutiveFailures = _consecutiveFailures,
        ConsecutiveReverts = _consecutiveReverts,
        SessionLossUSDC = _sessionLossUSDC,
        OpenedAt = _openedAt
    };

    // ============================================================
    // Private — trip logic
    // ============================================================

    private void EvaluateTrip(string reason)
    {
        var shouldTrip = false;
        var tripReason = "";

        // Trip 1: Too many consecutive failures
        if (_consecutiveFailures >= _options.MaxConsecutiveFailures)
        {
            shouldTrip = true;
            tripReason = $"{_consecutiveFailures} consecutive failures";
        }

        // Trip 2: Too many consecutive reverts
        if (_consecutiveReverts >= _options.MaxConsecutiveReverts)
        {
            shouldTrip = true;
            tripReason = $"{_consecutiveReverts} consecutive reverts";
        }

        // Trip 3: Session loss exceeds maximum
        if (_sessionLossUSDC >= _options.MaxSessionLossUSDC)
        {
            shouldTrip = true;
            tripReason = $"session loss ${_sessionLossUSDC:F2} exceeds maximum ${_options.MaxSessionLossUSDC:F2}";
        }

        if (shouldTrip)
            Trip(tripReason);
    }

    private void Trip(string reason)
    {
        _state = CircuitState.Open;
        _openedAt = DateTimeOffset.UtcNow;

        _logger.LogError(
            "⚡ CIRCUIT BREAKER TRIPPED — reason: {Reason} | " +
            "all trading halted for {Min} minutes | " +
            "consecutive failures: {Failures} | " +
            "session loss: {Loss:C}",
            reason,
            _options.CooldownMinutes,
            _consecutiveFailures,
            _sessionLossUSDC);
    }
}

// ============================================================
// Circuit breaker state
// ============================================================

public enum CircuitState
{
    Closed,   // Normal — trading allowed
    Open,     // Tripped — trading halted
    HalfOpen  // Recovering — one test trade allowed
}

public sealed class CircuitBreakerStatus
{
    public CircuitState State { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int ConsecutiveReverts { get; init; }
    public decimal SessionLossUSDC { get; init; }
    public DateTimeOffset? OpenedAt { get; init; }
}

// ============================================================
// Configuration options
// ============================================================

public sealed class CircuitBreakerOptions
{
    /// <summary>
    /// Number of consecutive trade failures before tripping.
    /// Default: 3
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>
    /// Number of consecutive reverted transactions before tripping.
    /// A revert means the contract executed but output < input.
    /// Default: 2
    /// </summary>
    public int MaxConsecutiveReverts { get; set; } = 2;

    /// <summary>
    /// Maximum total USDC loss in a single session before tripping.
    /// Default: $10 USDC
    /// </summary>
    public decimal MaxSessionLossUSDC { get; set; } = 10m;

    /// <summary>
    /// How many minutes to wait before attempting recovery.
    /// Default: 30 minutes
    /// </summary>
    public int CooldownMinutes { get; set; } = 30;

    /// <summary>
    /// Minimum USDC wallet balance — halt trading if balance drops below this.
    /// Default: $20 USDC
    /// </summary>
    public decimal MinWalletBalanceUSDC { get; set; } = 20m;
}
