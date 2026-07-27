namespace CivicSurvival.Core.Interfaces.Domain.Economy
{
    /// <summary>
    /// Read-only snapshot of shadow wallet state for callers that need to see
    /// balance + freeze + sanctions atomically. <c>Exists=false</c> is the
    /// canonical answer when the wallet singleton is unavailable (feature
    /// closed, pre-load window). Replaces the previous out-parameter API,
    /// which the null-object generator cannot synthesise (CIVIC405).
    /// </summary>
    public readonly struct WalletSnapshot
    {
        public bool Exists { get; }
        public long Balance { get; }
        public bool IsFrozen { get; }
        public float SanctionsMarkup { get; }

        public WalletSnapshot(bool exists, long balance, bool isFrozen, float sanctionsMarkup)
        {
            Exists = exists;
            Balance = balance;
            IsFrozen = isFrozen;
            SanctionsMarkup = sanctionsMarkup;
        }
    }
}
